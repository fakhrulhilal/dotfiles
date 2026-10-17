# Runtime and compio

Guide chapter: <https://docs.ntex.rs/4-runtime.html>. Sources: `ntex-rt/src/rt_compio.rs` and
`ntex-net/src/compio/` in the ntex repo.

## Execution model

- Each runtime thread (an `Arbiter`) runs one single-threaded event loop. A task stays on the thread that spawned
  it, so futures and services may hold `Rc`, `Cell`, `RefCell` and other `!Send` values.
- A server starts several workers, each an arbiter with its own runtime, to use more cores. Data shared between
  workers must be `Arc` plus atomics or locks.
- `#[ntex::main]` builds a `System` (the top-level runtime context, primary arbiter on the main thread) and drives the
  async `main`. `#[ntex::test]` does the same per test, with signal and panic handling off.

## Choosing the backend

`ntex::rt::DefaultRuntime` picks the runtime and the matching network reactor from Cargo features:

| Feature                         | Runtime                                                                         |
|---------------------------------|---------------------------------------------------------------------------------|
| `compio`                        | a compio runtime per runtime thread; compio picks the I/O driver                |
| `tokio`                         | a current-thread tokio runtime per thread, ntex tasks in a `LocalSet`           |
| none                            | native "neon": io_uring with polling fallback on Linux, polling elsewhere, IOCP |
| `neon-polling` / `neon-uring`   | force polling, or require io_uring, with the native runtime (never both)        |

With both `tokio` and `compio` enabled, tokio wins without a warning. Check what is enabled with
`cargo tree -e features -i ntex`.

The runtime feature can also be set on `ntex-rt`/`ntex-net` directly; enabling it on `ntex` forwards it.

### compio drivers

ntex 4.3 depends on `compio-runtime` 0.12 and `compio-driver` 0.12 (compio 0.19 on the facade). The log line
`ntex_net::compio: Starting compio runtime, driver Poll` (one per runtime thread) shows the driver in use.

| Platform | Driver                                                                                               |
|----------|------------------------------------------------------------------------------------------------------|
| Linux    | io_uring (compio-driver default feature `io-uring`); with the `polling` feature also enabled, compio uses a "fusion" driver that tries io_uring and falls back to epoll |
| macOS    | kqueue through `polling` (logged as `Poll`)                                                          |
| Windows  | IOCP                                                                                                 |

Without the `polling` feature, a Linux host where io_uring is unavailable makes `compio_runtime::Runtime::new()`
fail, and ntex unwraps that error, so every worker panics at start. That happens in containers: recent Docker
releases block the io_uring syscalls in their default seccomp profile, and some hardened kernels disable io_uring
(`kernel.io_uring_disabled`). Enable the fallback through the compio facade (Cargo unifies the feature into the
compio-driver that ntex uses):

```toml
compio = { version = "0.19", default-features = false, features = ["runtime", "io-uring", "polling"] }
```

Alternatives: allow io_uring in the container's seccomp profile, or build with the native runtime.

### Using compio APIs directly

ntex enters the compio runtime on every runtime thread, so compio's own APIs work inside handlers, services and
spawned tasks (verified with compio 0.19):

```rust
async fn readme() -> Result<String, std::io::Error> {
    let bytes = compio::fs::read("README.md").await?; // completion-based file I/O, io_uring on Linux
    Ok(String::from_utf8_lossy(&bytes).into_owned())
}

compio::time::sleep(std::time::Duration::from_millis(5)).await;
```

Keep `compio` on the minor version whose `compio-runtime` matches ntex-net's (0.19 for 0.12). With two
`compio-runtime` versions in the tree, the second has its own thread-local runtime that nothing starts.
`cargo tree -i compio-runtime` must list one version. compio buffer APIs take ownership of buffers
(`compio::buf::IoBuf`); prefer the convenience functions (`compio::fs::read`, `write`) in handlers.

## tokio-only crates under compio

There is no tokio reactor on a compio worker. Anything that registers with tokio's I/O driver or timer panics with
"there is no reactor running, must be called from the context of a Tokio 1.x runtime"; inside `ntex::rt::spawn` the
task ends with `JoinError`. That covers `tokio::time`, `tokio::net`, `tokio::fs`, `tokio::spawn` and the crates
built on them: sqlx with `runtime-tokio`, reqwest, hyper clients, tokio-postgres, deadpool on tokio, redis with
`tokio-comp`, tonic, aws-sdk, and so on. The blocking `postgres` crate is no way out: it wraps tokio-postgres and runs
a tokio runtime inside. Pure-computation tokio pieces, such as `tokio::sync` channels and mutexes, work, because they
need no reactor.

`compio::compat` (`compat-tokio`) goes the other way, running compio inside tokio, and does not help here.

Options, in order of preference:

1. Use the ntex or compio equivalent: `ntex::time`, `ntex::rt::spawn`, `ntex::client::Client` for HTTP,
   `compio::fs`, `compio::process`, `compio::net`.
2. Pick the crate's non-tokio runtime when it has one. Crates built on
   [async-io](https://github.com/smol-rs/async-io) (smol) work on compio workers as they are: async-io's reactor runs
   on a thread of its own, so its sockets and timers wake any executor. **sqlx** is the common case, see below.
3. A blocking driver on the blocking pool, the pattern of <https://ntex.rs/docs/databases> and the `db/diesel` and
   `db/r2d2` examples: rusqlite or diesel behind an r2d2 pool in the state, each query in `web::block` (or
   `ntex::rt::spawn_blocking`), `BlockingError` rendered as 500. Not `postgres`/`r2d2_postgres` (tokio inside).
4. Only for a crate with no other runtime: run a tokio runtime on its own threads and bridge with channels. `tokio::sync::oneshot` and `mpsc` receivers can
   be awaited on an ntex worker:

```rust
use std::sync::LazyLock;

static TOKIO: LazyLock<tokio::runtime::Runtime> = LazyLock::new(|| {
    tokio::runtime::Builder::new_multi_thread().worker_threads(2).enable_all().build().unwrap()
});

/// Runs a tokio-bound future on the dedicated runtime and awaits its result from an ntex worker.
async fn on_tokio<F>(fut: F) -> Result<F::Output, tokio::sync::oneshot::error::RecvError>
where
    F: Future + Send + 'static,
    F::Output: Send + 'static,
{
    let (tx, rx) = tokio::sync::oneshot::channel();
    TOKIO.spawn(async move {
        let _ = tx.send(fut.await);
    });
    rx.await
}

// let body = on_tokio(async move { client.get(url).send().await?.text().await }).await??; // a reqwest::Client
```

The future must be `Send`, so pass owned, `Send` values (cloned clients and pools work). Every call crosses threads;
if most of the app is tokio-bound, compio is the wrong backend, so use the `tokio` feature instead.

### sqlx on compio

sqlx 0.9 has `runtime-smol` (and `runtime-async-global-executor`), both on async-io. With it, sqlx runs on the workers
with no tokio in the tree at all, and the code is the same as on tokio (verified with PostgreSQL in Docker, ntex 4.3,
sqlx 0.9, under load):

```toml
sqlx = { version = "0.9", default-features = false, features = ["runtime-smol", "tls-rustls-ring-webpki", "postgres", "migrate", "macros"] }
```

- `PgPoolOptions::connect(..).await`, `sqlx::migrate!().run(&pool).await` and every query are awaited directly in
  `main`, the state factory or handlers. Keep the `PgPool` (`Send + Sync`, cheap to clone) shared by all workers.
- sqlx's own background tasks go to smol's global executor; its timeouts use async-io timers. Nothing to set up.
- Do not wrap sqlx in `web::block`. That is ntex's pattern for synchronous drivers (rusqlite, diesel); the closure
  must be synchronous, so an async query would need `async_io::block_on` inside it, which still needs
  `runtime-smol` for the socket and ties up a blocking-pool thread per query. Await sqlx directly instead.
- In a workspace where another member enables `runtime-tokio`, Cargo unifies both features into one sqlx. That is
  fine: sqlx checks per call for a current tokio runtime (`Handle::try_current`) and uses async-io when there is
  none, which is always the case on a compio worker.
- Check with `cargo tree -p <crate> -e all -i tokio`: it must find no package.
- The cost, measured against the same sqlx code on axum/tokio (PostgreSQL, pool of 50, 10 CPUs): about 5 to 10%
  less throughput under load and about +0.05 ms per request on one connection. It fits each reply crossing from
  async-io's reactor thread to the worker. With a small pool (10) the pool is the limit and both measure the same.

Crates with no tokio in their tree run directly on the workers. The embedded Turso engine (`turso` 0.8, local file
or `:memory:`) does: its async API needs no reactor.

If you choose the `tokio` backend: ntex enters an existing tokio runtime handle when there is one on the thread,
otherwise it creates a current-thread runtime per worker, so tokio crates work on workers; do not nest
`SystemRunner::block_on` inside a tokio task.

## Spawning

```rust
let handle = ntex::rt::spawn(async move { 10usize }); // !Send futures allowed, must be 'static
let value = handle.await.unwrap();                    // Err(JoinError) on panic or cancellation
```

- `spawn()` panics outside a running ntex system. While a compio runtime is shutting down, `spawn()` drops the
  future and the handle resolves to `JoinError`.
- Dropping a `JoinHandle` detaches the task, it does not cancel it. Use `cancel()`, `detach()`, `is_finished()`.
- Detached tasks are not guaranteed to finish before the arbiter or system stops.
- `Arbiter::handle().spawn(fut)` sends `Send + 'static` work to another runtime thread. On compio and tokio,
  cancelling that handle abandons the result but does not stop the remote task; use an explicit cancellation signal.

## Timers

```rust
use ntex::time::{Millis, Seconds, sleep, timeout};

sleep(Millis(10)).await;
let res = timeout(Seconds(5), some_future).await; // Err(()) on expiry
```

ntex timers have roughly 16 ms granularity and suit timeouts and scheduling, not measurement. Zero-duration `sleep`
or `timeout` still waits for one tick; `timeout_checked` treats zero as disabled. `ntex::time` works on every
backend; `compio::time` also works on compio and is finer-grained.

## Blocking work

```rust
let total = ntex::rt::spawn_blocking(|| (0..1_000u64).sum::<u64>()).await.unwrap();

// web::block wraps spawn_blocking for closures returning Result. Its error is BlockingError<E>, which
// DefaultError renders as 500; a custom error domain needs a WebResponseError impl for it.
let text = web::block(move || std::fs::read_to_string(path)).await?;
```

- The closure and result must be `Send`. The pool grows to 256 threads by default; idle threads exit after 60 s.
  Change with `System::build().thread_pool_limit(n).thread_pool_recv_timeout(dur)`.
- Outside a running system, `spawn_blocking` runs the closure immediately on the calling thread.
- Dropping the future prevents queued work from starting but cannot interrupt a running closure.

## System, Arbiter and the entry point

```rust
#[ntex::main(name = "api", signals = true, panic_handling = true, ping_interval = 250)]
async fn main() -> std::io::Result<()> { /* ... */ }
```

Options: `name`, `signals`, `panic_handling`, `ping_interval` (ms, 0 disables), `rt = Type` for a custom runner.
For settings without attribute options, build the system by hand:

```rust
fn main() -> std::io::Result<()> {
    ntex::rt::System::build()
        .name("api")
        .signals(true)
        .ping_interval(1_000)   // arbiter pings, ms, default 2_000, 0 disables
        .ping_threshold(500)    // a slower ping reply counts as a stall, default 1_000
        .thread_pool_limit(32)
        .build(ntex::rt::DefaultRuntime)
        .block_on(async { run().await })
}
```

- `block_on(fut)` drives one root future; `run(callback)` keeps the loop alive until `System::current().stop()`.
- `Arbiter::new()` / `with_name()` start another runtime thread; `stop()` and `join()` end it. `System::stop()`
  ends everything.
- `System::get_value()` stores a thread-safe value for the whole system; `Arbiter::get_value()` a per-thread clone
  (per-worker clients or caches).

## Diagnostics

Released ntex-rt 4.2.1 pings every spawned arbiter (`ping_interval`, default 2 s) and treats a reply slower than
`ping_threshold` (default 1 s) as a stall; on Linux it then tries to capture a backtrace of the busy arbiter
(needs `signals = true`, uses `SIGUSR2`). `System::list_arbiter_pings()` exposes the last round trips. Add
`debug = "line-tables-only"` to the release profile for readable frames.

The docs.ntex.rs runtime chapter describes more that exists only on the ntex `main` branch so far (ntex-rt 4.3):
latency sampling with `latency_interval`, per-runtime busy time and poll histograms, and server metrics. Even there,
`self_wakes`, `current_task` and busy-loop detection are neon-only; compio reports `None` for them.
