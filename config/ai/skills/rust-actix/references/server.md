# Server, runtime and operations

From the actix.rs pages Server, HTTP/2, Databases, Connection Lifecycle and Auto-Reloading, and the `HttpServer`
docs. Behaviour marked *(run)* was checked against actix-web 4.18.1 on macOS.

## Starting

```rust
#[actix_web::main] // a current-thread tokio runtime with a LocalSet (actix-rt System) on the main thread
async fn main() -> std::io::Result<()> {
    let state = web::Data::new(AppState::connect().await?);
    let server = HttpServer::new(move || app(state.clone()))
        .workers(4)
        .shutdown_timeout(8)
        .bind(("0.0.0.0", 8080))?;   // fails here if the port is taken
    tracing::info!(addresses = ?server.addrs(), "listening"); // the real port after binding port 0
    server.run().await
}
```

`#[tokio::main]` works too: `run()` returns a `Server` future that can be awaited or `tokio::spawn`ed. The workers
always get runtimes of their own.

| `HttpServer` method                         | Default / notes                                                          |
|---------------------------------------------|--------------------------------------------------------------------------|
| `workers(n)`                                | `std::thread::available_parallelism()` (logical CPUs; a cgroup CPU quota counts), at most 512 |
| `bind(addr)`, `listen(std_listener)`, `bind_uds(path)` | may be called several times                                   |
| `bind_rustls_0_23(addr, ServerConfig)`, `bind_openssl(addr, SslAcceptorBuilder)` | need `rustls-0_23` / `openssl` (and `http2` for h2 via ALPN) |
| `bind_auto_h2c(addr)`                       | HTTP/1 and cleartext HTTP/2 (prior knowledge) on one port; needs `http2`  |
| `keep_alive(Duration \| KeepAlive::Os \| KeepAlive::Disabled)` | 5 s                                     |
| `client_request_timeout(d)`                 | time to receive the request head, 5 s                                    |
| `client_disconnect_timeout(d)`              | time to close after the response, 1 s                                    |
| `max_connections(n)`                        | concurrent connections per worker, 25 000                                |
| `max_connection_rate(n)`                    | concurrent TLS handshakes per worker, 256                                |
| `backlog(n)`                                | 1024                                                                     |
| `shutdown_timeout(secs)`                    | 30: how long a graceful stop waits for open requests                     |
| `disable_signals()`                         | no signal handling (tests, embedding)                                    |
| `shutdown_signal(fut)`                      | stop gracefully when `fut` completes; replaces the OS signal handlers    |
| `system_exit()`                             | stop the actix `System` when the server stops                            |
| `worker_max_blocking_threads(n)`            | `web::block` pool size per worker, 512 / logical CPUs                    |
| `on_connect(f)`                             | put connection data (TLS info, socket) into request extensions           |

## Workers and state

Each worker is an OS thread with a current-thread tokio runtime. The `App` factory runs once per worker, so:

- values built in the factory are per worker; values built in `main` and cloned in are shared. `web::Data` is an
  `Arc`; a per-worker `RefCell` counter and a shared `AtomicU64` behave differently with `workers(4)`;
- a future spawned with `actix_web::rt::spawn` (or `tokio::spawn`) runs on the current worker's runtime and need not
  be `Send` with `rt::spawn`;
- an async client (sqlx `PgPool`, reqwest) created in `main` and shared is fine: sqlx's pool hands out connections
  to whichever worker asks.

## Blocking work

A worker serves its requests one future at a time on one thread. Blocking (Diesel, `std::fs` loops, password
hashing, `std::thread::sleep`) stops every request on that worker.

```rust
let user = web::block(move || {
    let mut conn = pool.get()?;          // r2d2 checkout blocks too: keep it inside
    insert_user(&mut conn, &name)
})
.await??;                                 // BlockingError, then the closure's own error
```

`web::block` runs the closure on the worker's blocking pool and returns `Result<R, BlockingError>`. For async
drivers (sqlx, SeaORM, tokio-postgres) await them directly; `web::block` is not needed.

## Databases

- **sqlx**: create the pool once in `main` (`PgPoolOptions::new().max_connections(n).connect(url).await?`), register
  it in `Data` (or `ThinData<PgPool>`, since a pool is already cheap to clone), await queries in handlers. Run
  migrations before `HttpServer::new`.
- **SeaORM**: the same with `DatabaseConnection`.
- **Diesel / rusqlite** (synchronous): an r2d2 pool in `Data`, every query inside `web::block`.

Size the pool for all workers together, not per worker.

## Stopping

| Signal (Unix) | Effect                                                                            |
|---------------|-----------------------------------------------------------------------------------|
| SIGTERM       | graceful: stop accepting, let open requests finish for `shutdown_timeout` *(run)* |
| SIGINT (Ctrl+C) | fast: workers are stopped at once                                              |
| SIGQUIT       | fast                                                                              |

On Windows only Ctrl+C is handled. For a container, `shutdown_timeout` must be shorter than the orchestrator's kill
delay (Docker: 10 s). For custom control:

```rust
let server = HttpServer::new(..).disable_signals().bind(..)?.run();
let handle = server.handle();          // ServerHandle: pause(), resume(), stop(graceful: bool)
actix_web::rt::spawn(async move {
    shutdown_requested().await;
    handle.stop(true).await;
});
server.await
```

Logs on a graceful stop come from `actix_server` ("SIGTERM received; starting graceful shutdown", "accept thread
stopped"); `run().await` returns after the workers finish *(run)*.

## TLS and HTTP/2

```toml
actix-web = { version = "4.18", default-features = false, features = ["macros", "http2", "rustls-0_23"] }
rustls = "0.23"
rustls-pemfile = "2"
```

```rust
let config = rustls::ServerConfig::builder().with_no_client_auth().with_single_cert(certs, key)?;
HttpServer::new(app).bind_rustls_0_23(("0.0.0.0", 8443), config)?.run().await
```

h2 is negotiated through ALPN when `http2` is enabled. Behind a TLS-terminating proxy, plain `bind` and
`req.connection_info()` (reads `Forwarded` / `X-Forwarded-*`) give the original scheme and host; only trust them
when the proxy sets them.

## Containers

- Static musl builds work (`x86_64-unknown-linux-musl`, `aarch64-unknown-linux-musl`); drop the default features if
  the brotli/zstd C code is not wanted.
- Bind `0.0.0.0` inside the container. Set `workers(n)` explicitly when the CPU quota is fractional.
- Health check endpoint: a plain `GET` route; probe with a static client binary in `FROM scratch` images.

## Development loop

`watchexec -e rs -r cargo run` restarts on change. actix's own docs advise against `systemfd` / `listenfd` socket
passing now.
