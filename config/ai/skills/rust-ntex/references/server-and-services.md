# Server, configuration and the service model

Guide chapters 1 to 3 (service model, pipelines, state), 5 (server), 6 (worker and request state) and 7 (I/O) at
<https://docs.ntex.rs>. Examples: `server/*`, `state/process`, `state/state-accumulation`, `tls/*` in
<https://github.com/ntex-rs/examples>.

## Server

`listeners -> accept thread -> worker-local service`. The server owns sockets and workers and knows nothing about
the protocol; the service built per worker handles each accepted `Io`.

```rust
ntex::server::build()                         // no worker state; factory receives &()
// ntex::server::build_with_config(cfg)       // cfg: ServerAppConfig, factory receives &State
    .name("api")
    .workers(4)                               // default: logical CPUs (all processor groups on Windows)
    .backlog(1024)                            // for listeners bound after this call
    .max_connections(20_000)                  // per worker, process-wide setting, default 25_600
    .graceful_shutdown_timeout(Seconds(15))   // default 30 s
    .bind("http", "0.0.0.0:8080", SharedCfg::new("HTTP"), async |_| HttpService::new(app.build()))?
    .run()                                    // Server: cloneable controller, await = until stopped
    .await
```

- `bind(name, addr, SharedCfg, factory)`: the name labels logs and metrics. Every address the name resolves to is
  bound; pass a concrete `SocketAddr` for exactly one listener. `listen()` takes an existing `std::net::TcpListener`
  (socket activation, custom options). `bind_uds`/`listen_uds` for Unix sockets.
- The factory is `Send + Clone + 'static`, called on every worker and again after restarts; the service it returns
  stays on that worker and need not be `Send`.
- `run()` panics without a listener. Workers stop receiving connections when not ready (connection limit, a
  service not ready); with none ready the accept loop pauses and connections wait in the OS backlog. ntex never
  rejects connections at the limit.
- Other settings: `stop_on_panic()`, `graceful_shutdown()` (panics, SIGQUIT and fatal signals stop gracefully),
  `disable_signals()`, `stop_runtime()` (stop the whole `System` with this server), `status_handler()` (accept-loop
  pause/resume notifications), `enable_affinity()` (pin workers to cores).
- Main branch only (documented on docs.ntex.rs, not in ntex 4.3.0 / ntex-server 4.2.0): `affinity(Affinity::..)`,
  which replaces `enable_affinity()` and is on by default, and `load_balancing(LoadBalancing::LeastLoaded |
  RoundRobin)`. Released versions distribute connections round-robin.

### Controlling and stopping

```rust
let server = web::server(...).bind(...)?.run();
let control = server.clone();
ntex::rt::spawn(async move {
    control.pause().await;      // stop accepting, keep connections
    control.resume().await;
    control.stop(true).await;   // graceful: close listeners, drain workers up to the timeout
});
server.await
```

| Signal                  | Default                                                       |
|-------------------------|---------------------------------------------------------------|
| `SIGTERM`               | graceful stop                                                 |
| `SIGINT` / Ctrl-C       | immediate stop                                                |
| `SIGQUIT`, panics       | immediate, graceful with `graceful_shutdown()`                |
| `SIGHUP`                | ignored                                                       |

Signals belong to the `System`. With several servers in one system, or your own signal policy, call
`disable_signals()` on each and stop the controllers yourself. Leave server-level shutdown time for each
connection's `IoConfig::set_shutdown_timeout()` (default 1 s) to finish.

### Metrics (main branch only)

Not in ntex-server 4.2.0, the release current with ntex 4.3.0; check that `Server::metrics()` exists in your version
before using it. `server.metrics()` returns a cheap `ServerMetrics` snapshot: accept loop (`accept_pauses`,
`accept_paused_duration`, `accept_backlog`), listeners (`accepted`, `connection_errors`, `resource_errors`) and per
worker (`items`, `active_connections`, `not_ready_duration`, `restarts`, `runtime`, `io`, `page_cache`). Counters are
totals since start: diff two snapshots for rates. Growing `accept_paused_duration` means all workers are at
capacity; any `restarts` or `latency.stalls` deserves an alert. Close reasons per connection
(`keepalive`, `read_timeout`, `write_timeout`, `peer_gone`, `decoder`, `service`, `shutdown`, `Custom1..8`) are in
`io[..].closed`.

## Worker state (`ServerAppConfig`)

```rust
struct AppConfig { db_url: String }                          // process-wide, Send + Sync

impl ntex::server::ServerAppConfig for AppConfig {
    type State = AppState;                                    // Clone, may hold Rc/RefCell
    async fn create(&self) -> std::io::Result<AppState> {     // on every worker start and restart
        Ok(AppState { pool: Rc::new(Pool::connect(&self.db_url).await?) })
    }
}
```

An `async || -> io::Result<State>` closure also implements it, and `NoConfig` gives `()` state. State goes to the
factory as `&State` and becomes the pipeline state of the connection service: `async fn handle(st: &AppState, io: Io)`.

## Connection and protocol configuration (`SharedCfg`)

```rust
let cfg = SharedCfg::new("API")
    .add(IoConfig::new()
        .set_shutdown_timeout(Seconds(2))          // connection close/flush budget, default 1 s, zero panics
        .set_keepalive_timeout(Seconds(30))        // non-HTTP/1 protocol dispatchers
        .set_frame_read_rate(Seconds(2), Seconds(10), 1024)
        .set_write_timeout(Seconds(10))            // close peers that stop reading
        .set_read_backpressure(32 * 1024)
        .set_write_backpressure(32 * 1024))
    .add(HttpServiceConfig::new()
        .set_keepalive_timeout(Seconds(30))        // or .set_keepalive(KeepAlive::Os / Disabled)
        .set_client_timeout(Seconds(5))            // time to receive the first request head
        .set_write_timeout(Seconds(10))
        .set_max_headers(64)
        .set_host_validation(true))
    .add(WebAppConfig::new().set_state(JsonConfig::default().limit(64 * 1024)));
```

The `SharedCfg` is cloned onto every accepted `Io`, and each layer reads the type it understands, falling back to
defaults (and logging "Configuration ... does not exist, using default" once per worker) when absent. The HTTP/1
dispatcher uses `HttpServiceConfig`, not the `IoConfig` keep-alive or read-rate settings. Change one setting at a
time because of an observed workload; backpressure marks are watermarks, not hard memory limits.

## TLS

```toml
ntex = { version = "4.3", features = ["compio", "rustls"] }
rustls = "0.23"
```

`web::server(...).bind_rustls(addr, &rustls::ServerConfig, cfg)` sets ALPN to `h2` and `http/1.1`.
`bind_openssl(addr, acceptor_builder, cfg)` with the `openssl` feature. On the lower level, wrap the HTTP service:
`ntex::http::rustls(config, &["h2", "http/1.1"], HttpService::new(app))` or `ntex::http::openssl(acceptor, ...)`.
Limit concurrent handshakes with `HttpServer::max_tls_handshakes(n)`. rustls needs a crypto provider: enable
`ntex-tls/rustls-ring` (or install a default provider) when rustls cannot pick one.

## The `Service` trait

```rust
trait Service<St, Req> {
    type Res;
    type Error;
    async fn ready(&self, ctx: Ctx<'_, Self, St>) -> Result<(), Self::Error>;          // default: ready
    async fn call(&self, req: Req, ctx: Ctx<'_, Self, St>) -> Result<Self::Res, Self::Error>;
    async fn shutdown(&self, ctx: Ctx<'_, Self, St>);                                  // default: nothing
}
```

- `St` is pipeline state, borrowed through `ctx.st()`; the compiler checks a service only runs with compatible state.
- `ready()` is backpressure: a service at capacity waits there. `Pipeline` coordinates readiness across concurrent
  calls; a successful readiness check is remembered for the next call.
- A wrapper calls inner services with `ctx.call(&self.inner, req)` (waits for readiness). `ctx.call_nowait` skips the
  check, only when readiness is already established. `ctx.ready()`/`ctx.shutdown()` exist for combinators.
- `ntex::forward_ready!(St, field)` / `forward_shutdown!(St, field)` implement the two lifecycle methods by
  delegation; without them a wrapper looks always ready and never shuts its inner service down.

## Function services and pipelines

```rust
use ntex::{Pipeline, service::{fn_service_st, map_state}};

async fn format_value(st: &AppState, value: usize) -> Result<String, Infallible> {
    Ok(format!("{}-{value}", st.prefix))
}

let svc = Pipeline::new(AppState { prefix: "item" }, format_value);  // async fn(&St, Req) is a service
assert_eq!(svc.call(10).await.unwrap(), "item-10");
let owned = svc.call_static(11);                                    // owned future, can move to a task

let chain = ntex::service(authenticate).and_then(load).and_then(execute);  // types must line up, errors equal
let wrapped = map_state(Metrics { .. }, record);                     // fixed inner state inside another pipeline
```

- `fn_service_st(f)` names the conversion explicitly (filters, chains). Its `and_then` comes from the `Service`
  trait, so `use ntex::Service;`, or start the chain with `ntex::service(f)`, whose builder has it inherently.
  `ntex::service::fn_service(f)` wraps a stateless `async fn(Req)`.
- Combinators on chains: `and_then`, `then`, `map`, `map_err`. A chain is itself a service and can be wrapped in
  middleware.
- `PipelineState::new(svc)` holds a service without state; callers pass `&state` per call.
- Unit-test services without a server: `Pipeline::new(test_state, fn_service_st(f)).call(req)`; mock the state's
  dependencies (examples `tests/mockall`, `tests/shimforge`).

## Request state accumulation and lower-level HTTP

`State<St, Req>` (or `(St, Req)`) carries per-connection state with the value through a chain; `unpack()` splits it.
`HttpService` accepts any `RequestState` input and gives its state to the HTTP handler or `web::App`:

```rust
ntex::server::build_with_config(app_config)
    .bind("http", "0.0.0.0:8080", SharedCfg::new("S"), async |_: &AppState| {
        ntex::service(collect_peer)            // async fn(&AppState, Io) -> io::Result<State<Conn, Io>>
            .and_then(HttpService::new(handle)) // async fn(&Conn, http::Request) -> io::Result<http::Response>
    })?
    .run()
    .await
```

A plain `Io` implements `RequestState` with `()` state. A `web::App` used as that handler gets the connection state
as its application state (`App::<Conn>::new()...build()`), or its own with `.build_with(state)`.

## HTTP client (`ntex::client`)

Runtime-neutral, so it is the HTTP client to use on compio.

```rust
use ntex::client::{Client, ClientConfig};

let client = Client::builder().build(
    SharedCfg::new("client").add(
        ClientConfig::new()
            .set_response_timeout(Seconds(10))
            .set_h1_connection_limit(8)
            .set_response_payload_limit(256 * 1024)
            .set_bearer_auth("token")?,                // header setters return Result<_, HttpError>
    ),
);                                                     // or Client::new() for defaults
let mut res = client.get("https://example.com/api").send().await?;
let body: Value = res.json().await?;                   // .body().await for bytes
let res = client.post(url).send_json(&payload).await?;
```

HTTPS needs the `rustls` or `openssl` feature. Build one client per worker (in the state factory) and reuse it; it
pools connections. Defaults such as headers, auth and timeouts belong in `ClientConfig`. WebSocket client:
`ntex::ws::WsClient::new(url, WsClientConfig::new()).connect()`.

## Test servers

- `ntex::server::test_server(async || HttpService::new(...))` starts one worker on a free port with signals off;
  `server.addr()`, `server.connect()` (client `Io`), dropping the last clone stops it (blocks briefly).
- For web apps prefer `web::test::server` (see the web reference).