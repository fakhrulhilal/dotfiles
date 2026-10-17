---
name: rust-ntex
description: "Build Rust HTTP services with ntex 4 running on the compio runtime (io_uring on Linux, IOCP on Windows, kqueue on macOS) instead of tokio: #[ntex::main], web::server_with_config and per-worker state, App/scope/resource routing, to() and to_with_state() handlers, extractors and responders, filters that change the request state type, middleware written as Middleware + Service with Ctx, error domains through web::State::Error and WebResponseError (JSON problem details), SharedCfg with IoConfig and HttpServiceConfig, the ntex HTTP client, WebSockets, graceful shutdown and web::test. Use when learning ntex, starting an ntex project, choosing or switching its runtime (compio, tokio, neon), or writing, reviewing or debugging code that imports `ntex` (web::App, web::resource, HandlerSt, WebResponseError, Pipeline, Ctx, forward_ready!, fn_service_st), including tokio-only crates that panic under compio and handler signatures that do not compile."
license: MIT
---

# ntex 4 on compio

ntex is a framework for composable network services. Everything, from a TCP connector to a web handler, is a
`Service` that turns a request into a response, and services compose into pipelines. A server runs one
single-threaded runtime per worker thread, so state and futures inside a worker need not be `Send` (`Rc`,
`RefCell` and `Cell` are normal), while anything shared between workers must be `Arc` and thread-safe.

This skill uses the **compio** backend: every worker runs a compio runtime, which uses io_uring on Linux, IOCP on
Windows and kqueue polling on macOS. Written against ntex 4.3.0 (ntex-service 5.2, ntex-rt 4.2, compio 0.19 /
compio-runtime 0.12), Rust 1.97 or newer. Only ntex 4 is covered; check `Cargo.lock` and confirm anything
surprising against the sources below.

## Where to look

| Need                                 | Source                                                                                 |
|--------------------------------------|----------------------------------------------------------------------------------------|
| Concepts, in depth (the guide)       | <https://docs.ntex.rs>, sources in `ntex/docs/` of <https://github.com/ntex-rs/ntex>   |
| Task-oriented docs with examples     | <https://ntex.rs/docs>, sources in <https://github.com/ntex-rs/website> (`examples/`)  |
| API reference                        | <https://docs.rs/ntex> (released), <https://docs.ntex.rs/api/> (`main` branch)         |
| Runnable apps                        | <https://github.com/ntex-rs/examples>; `state/web-state` is the canonical ntex 4 app    |
| What changed per release             | `ntex/CHANGES.md` in the ntex repo                                                     |

docs.ntex.rs and the repo's `main` branch run ahead of crates.io: server metrics, `affinity()`, `load_balancing()`
and the runtime latency sampling it describes are not in ntex 4.3.0. Confirm an API on docs.rs for the version in
`Cargo.lock` before relying on it.

None of the upstream examples use compio. They select tokio or the native runtime, and they port by changing the
runtime feature, except where they call tokio crates (see the runtime rules below).

Read the reference that matches the task before writing code:

- [references/runtime-compio.md](references/runtime-compio.md): runtime backends, compio setup and drivers, spawning,
  timers, blocking work, using tokio-only crates, `System` and `Arbiter`, containers.
- [references/web.md](references/web.md): routing, state, handlers, extractors, responders, filters, middleware, error
  domains, testing, static files, WebSockets.
- [references/server-and-services.md](references/server-and-services.md): server builder, workers, `SharedCfg`
  settings, TLS, signals and shutdown, metrics, the `Service` trait, pipelines, `Ctx`, connection state, the HTTP
  client.
- [references/pitfalls.md](references/pitfalls.md): compile errors and runtime surprises already hit, with fixes.

`assets/template/` is a complete, tested todo JSON API on ntex 4 + compio: per-worker state, a scope with resources,
a middleware, a filter that types the request state, an error domain that renders every failure as
`application/problem+json`, and in-memory plus real-HTTP tests. Copy it to start a project, and read it instead of
writing these pieces from memory.

## Start a project

```toml
[dependencies]
ntex = { version = "4.3", features = ["compio"] }
# For calling compio APIs (fs, time, process) and for `polling`, which lets Linux fall back from io_uring to epoll
# (containers) instead of panicking. Keep it on the compio-runtime that ntex-net uses: 0.19 = compio-runtime 0.12.
compio = { version = "0.19", default-features = false, features = ["runtime", "io-uring", "polling", "fs", "time"] }
serde = { version = "1", features = ["derive"] }
```

`ntex`'s default features are `ws` only. Add `cookie`, `compress` (gzip, deflate, zstd), `rustls` or `openssl` when
needed. Check that the backend really is compio: `cargo tree -i compio-runtime` must show one version, and the log
prints `Starting compio runtime, driver ...` once per worker.

## Shape of an app

```rust
use std::{cell::Cell, io, rc::Rc, sync::Arc};

use ntex::web::{self, App, HttpResponse};
use ntex::SharedCfg;

struct Config {
    greeting: String,
}

#[derive(Clone)]
struct AppState {
    config: Arc<Config>,      // process-wide: created before the server, cloned into every worker
    hits: Rc<Cell<u64>>,      // worker-local: no locking, never crosses threads
}

impl web::State for AppState {
    type Error = web::DefaultError; // the error domain; a custom type renders errors your way
}

async fn index(st: &AppState, _req_state: (), name: web::types::Path<String>) -> String {
    st.hits.set(st.hits.get() + 1);
    format!("{} {name}", st.config.greeting)
}

#[ntex::main]
async fn main() -> io::Result<()> {
    let config = Arc::new(Config { greeting: "Hello".into() });
    web::server_with_config(
        // State factory: runs on each worker (again if the worker restarts).
        async move || Ok(AppState { config: config.clone(), hits: Rc::new(Cell::new(0)) }),
        // App factory: runs per worker with that worker's state.
        async |_: &AppState| {
            App::new()
                .middleware(web::middleware::Logger::default())
                .route("/hello/{name}", web::get().to_with_state(index))
                .route("/health", web::get().to(async || HttpResponse::Ok()))
        },
    )
    .bind("127.0.0.1:8080", SharedCfg::new("API"))?
    .run()
    .await
}
```

Without state use `web::server(async |_| App::new()...)`; `App<()>` uses `DefaultError` automatically.

## Rules that prevent most mistakes

Runtime (compio):

- Enable only the `compio` runtime feature. If anything enables `ntex/tokio` too, tokio silently wins; check with
  `cargo tree -e features -i ntex`.
- Never call tokio-bound code on a worker: `tokio::time`, `tokio::net`, `tokio::spawn`, and crates built on them
  (sqlx with `runtime-tokio`, reqwest, tokio-postgres, the blocking `postgres` crate, deadpool with tokio, most redis
  and gRPC clients). They panic with no reactor running. Use `ntex::time`, `ntex::rt::spawn`,
  `ntex::client::Client`, compio APIs, a crate's async-io runtime (**sqlx: `runtime-smol`**, same code, no tokio),
  blocking drivers through `web::block` with an r2d2 pool (ntex.rs/docs/databases), and only as a last resort a
  tokio runtime on its own threads behind channels (runtime reference).
- Prefer `ntex::time::{sleep, timeout}` (backend-neutral, ~16 ms granularity). `compio::fs` and `compio::time` work
  inside handlers because ntex enters the compio runtime on every worker.
- Never block a worker: no `std::thread::sleep`, synchronous I/O or long CPU loops between `.await`s. One blocked
  worker stalls every connection on it.

State:

- `server_with_config` creates the state once per worker and clones it for every connection. Plain fields are copied;
  put anything that must be shared behind `Rc` (one worker) or `Arc` (all workers, created outside the factory).
- Do not hold a `RefCell` borrow or a `MutexGuard` across `.await`.
- Handlers that need state use `to_with_state()` and take `(&St, request_state, extractors...)`. `to()` handlers
  take extractors only. There is no state extractor.
- `WebAppConfig::set_state()` values are thread-safe configuration read with `req.app_state::<T>()` (and extractor
  limits like `JsonConfig`). They are not the application state.

Routing:

- Settings (`with_config`, `middleware`, `filter`, `case_insensitive_routing`, `external_resource`) come before the
  first `route`/`service`/`configure`/`default_service` on `App`, `Scope` and `Resource`; afterwards they do not
  compile.
- Group the methods of one path in `web::resource(path).route(..).route(..)`: a wrong method then gets the resource's
  405. `App::route()`/`Scope::route()` makes one resource per call, so a wrong method falls through to 404.
- A matched scope never uses the app's `default_service`; give each scope its own. Unmatched paths and methods
  answer with an empty body unless you set default services.
- Registration order wins: put `/files/status` before `/files/{path}*`. Trailing slashes matter. `GET` does not add
  `HEAD` or `OPTIONS`.
- Path values are percent-decoded after splitting (`%2F` becomes `/` inside one value); `{tail}*` is not decoded.
  Validate before using them as file names or keys.

Handlers and errors:

- `State::Error` picks the error domain. With a custom domain every error that can reach a response needs
  `impl WebResponseError<AppState, MyDomain>`: your handler errors, extractor errors (`JsonPayloadError`,
  `PathError`, `QueryPayloadError`, `UrlencodedError`, `PayloadError`) and responder errors (`serde_json::Error` for a
  `Json<T>` response). A missing one is a compile error at the route.
- A `to_with_state()` handler returning `impl Responder<St>` needs `+ use<>` (edition 2024 captures the `&St`
  lifetime otherwise, and the handler "does not implement HandlerSt").
- Extractor and handler errors become responses inside the route, so middleware sees them as responses. Errors from
  filters and middleware travel as `Err` and are rendered outside all middleware.
- `web::error::ErrorBadRequest(..)` and friends put their message in the response body. Never wrap internal errors
  in them; log and return a safe message.
- Renderers never see the request. For a problem's `instance`, store the problem in the response's extensions and
  finish it in a middleware (web reference, Errors).
- `ntex-cors` refuses disallowed origins with an error (400) instead of omitting the header, needs a
  `WebResponseError` impl for `CorsError` in a custom domain, and panics at `build()` on an origin it cannot parse:
  validate origins at startup (web reference, Middleware).

Middleware and filters:

- Middleware runs in registration order on the way in (the first registered is outermost).
- In a middleware service, call the inner service with `ctx.call(&self.service, req)`, store it in a named field,
  and add `ntex::forward_ready!(St, service)` and `ntex::forward_shutdown!(St, service)`.
- `Middleware::create()` runs every time the application service is built, which is once per connection. Keep it
  cheap and put expensive setup in the state factory.
- Use a filter (`App::filter(fn_service_st(f))` with `async fn f(&St, WebRequest<In>) -> Result<WebRequest<Out>, E>`)
  for request-only work and to change the request state type, for example to an authenticated user. Handlers that
  require that type then cannot be registered where the filter does not run.

Server and tests:

- Defaults: one worker per logical CPU, SIGTERM stops gracefully (30 s timeout), Ctrl-C stops immediately.
- Add `IoConfig` and `HttpServiceConfig` to the `SharedCfg` passed to `bind()`: they hold timeouts and limits, and
  without them every worker logs "Configuration ... does not exist, using default".
- Share the app between `main` and tests with `pub fn app() -> impl ServiceFactory<AppState, Request, ...>` that ends
  in `.build()` (template `src/lib.rs`). Test in memory with `web::test::init_service_st(state, app())`, and over real
  HTTP with `web::test::server(async |_| ntex::service::map_state_factory(state, app()))`.
- Tests are `#[ntex::test] async fn`. `ntex::test` and `ntex::main` start the selected backend, compio here.

## Working method

1. Check versions and features: `ntex` in `Cargo.lock`, `cargo tree -i compio-runtime`, and that no crate in the
   tree needs a tokio reactor at runtime.
2. Start from `assets/template/` or the closest upstream example, and copy its shape instead of inventing an API.
3. `cargo build`. Long trait errors usually mean a missing `WebResponseError` impl, a handler whose request-state
   argument does not match the filters before it, a missing `+ use<>`, or a builder setting placed after a route.
   references/pitfalls.md maps the messages to fixes.
4. `cargo test`, then run it and exercise the routes with curl, including wrong methods, unknown paths, malformed
   and oversized bodies.

## Learning path

When someone is learning ntex rather than shipping a change, go in this order. After each step have them change the
template or an example so the idea is used, not only read.

1. Getting started on ntex.rs, then run `assets/template` and curl it. Exercise: add a `GET /api/v1/todos/count`.
2. Guide chapters 1 to 3 (service model, pipelines, state). Exercise: write `async fn(&St, Req)` services, chain
   them with `ntex::service(a).and_then(b)` and call the chain through `Pipeline::new(state, chain)`.
3. Guide chapter 4 (runtime) with references/runtime-compio.md. Exercise: `spawn`, `spawn_blocking`, a `timeout`, and
   reading a file with `compio::fs` in a handler.
4. Guide chapters 8.1 and 8.2 (routing, state). Exercise: a scope with its own default service, and a per-worker
   counter next to a process-wide one; watch them differ with `workers(2)`.
5. Guide chapters 8.3 to 8.5 (filters, middleware, extractors, errors). Exercise: an auth filter that turns
   `WebRequest<()>` into `WebRequest<User>` and a handler that takes `User`, then try to register it outside the
   filter and read the compile error.
6. Guide chapters 5 and 6 (server, worker state). Exercise: `ServerAppConfig`, graceful shutdown with SIGTERM, and
   server metrics.
7. Guide chapters 7 and 9 (I/O layer, byte buffers) only when writing protocols or tuning; the `ntex-rs/examples`
   `websockets/` and `server/` apps show lower-level use.