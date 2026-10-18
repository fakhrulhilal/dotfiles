# Middleware

Middleware is an ordinary handler attached with `hoop()`. It runs before the handlers below it, and its code after
`ctrl.call_next(..)` runs on the way out (onion model). Scope rules are in routing.md: router hoops run only for
matched routes, service hoops for every request.

## Writing middleware

```rust
use std::time::Instant;
use salvo::prelude::*;

#[handler]
async fn timing(req: &mut Request, depot: &mut Depot, res: &mut Response, ctrl: &mut FlowCtrl) {
    let start = Instant::now();
    ctrl.call_next(req, depot, res).await;
    tracing::info!(path = req.uri().path(), status = ?res.status_code, ms = start.elapsed().as_millis() as u64);
}

#[handler]
async fn require_key(req: &mut Request, depot: &mut Depot, res: &mut Response, ctrl: &mut FlowCtrl) {
    match req.header::<String>("x-api-key") {
        Some(key) if key == "secret" => depot.insert("caller", key),
        _ => {
            res.render(StatusError::unauthorized());
            ctrl.skip_rest();
        }
    };
}
```

- A middleware that does not call `call_next` lets the chain continue anyway after it returns (the next handler
  runs), unless it stopped the chain. Call `call_next` explicitly when you need code after the inner handlers.
- Stopping: `ctrl.skip_rest()` skips everything after this handler. Rendering a 4xx/5xx or 3xx status also stops
  the chain (verified: a hoop rendering `StatusError::forbidden()` keeps the route's handler from running).
  `ctrl.cease()` additionally marks the flow as ceased (`ctrl.is_ceased()`), for outer middleware to check.
- Middleware that takes configuration is a struct implementing `Handler` (`#[async_trait]`), or a function
  returning `impl Handler`.

Order: hoops run in registration order on the way in and reverse order on the way out; service hoops wrap router
hoops. Put `CatchPanic` and logging outermost, then CORS, rate limits and auth, then timeouts and concurrency
limits closest to the work, so rejected requests do not hold permits.

## Built-in middleware

| Type / function                                                | Feature               | Path                         | Rejects with       |
|----------------------------------------------------------------|-----------------------|------------------------------|--------------------|
| `Logger::new()`                                                | `logging`             | `salvo::logging`             |                    |
| `CatchPanic::new()`                                            | `catch-panic`         | `salvo::catch_panic`         | 500                |
| `RequestId::new()`                                             | `request-id`          | `salvo::request_id`          |                    |
| `Compression::new()`                                           | `compression`         | `salvo::compression`         |                    |
| `Timeout::new(dur)`                                            | `timeout`             | `salvo::timeout`, prelude    | 503                |
| `max_concurrency(n)`                                           | `concurrency-limiter` | `salvo::concurrency_limiter` | 429                |
| `max_size(bytes)`                                              | `size-limiter`        | `salvo::size_limiter`        | 413                |
| `CachingHeaders::new()`, `ETag`, `Modified`                    | `caching-headers`     | `salvo::caching_headers`     | 304 (not an error) |
| `Cache::new(store, issuer)`                                    | `cache`               | `salvo::cache`               |                    |
| `Cors::new()..into_handler()`                                  | `cors`                | `salvo::cors`                | security.md        |
| `RateLimiter::new(..)`                                         | `rate-limiter`        | `salvo::rate_limiter`        | 429, security.md   |
| `JwtAuth`, `BasicAuth`, `SessionHandler`, `Csrf`, flash stores | see security.md       |                              |                    |
| `ForceHttps::new()`                                            | `force-https`         | prelude                      | redirect           |
| `add_slash()`, `remove_slash()`                                | `trailing-slash`      | `salvo::trailing_slash`      | redirect           |
| `affix_state::inject(v)`                                       | `affix-state`         | `salvo::affix_state`         |                    |

`size-limiter` is not part of `full`; name it explicitly.

## Compression

```rust
use salvo::compression::{Compression, CompressionLevel};
use salvo::http::mime;

let compression = Compression::new()        // brotli, zstd, gzip, deflate; min_length 1024; text-like types
    .disable_all()
    .enable_brotli(CompressionLevel::Minsize)
    .enable_gzip(CompressionLevel::Fastest)
    .min_length(2048)
    .content_types(&[mime::TEXT_HTML, mime::APPLICATION_JSON, "image/svg+xml".parse().unwrap()]);
```

Levels: `Fastest`, `Default`, `Minsize` (there is no `Best`), `Precise(n)`. `content_types` takes `&[Mime]`, not
strings. By default the client's q-values pick the algorithm; `.force_priority(true)` uses the server's order.
Compression comes before `Cache` in the chain so the stored body is already compressed.

## Timeout

```rust
let router = Router::new()
    .hoop(Timeout::new(Duration::from_secs(30)).error(|| StatusError::gateway_timeout().brief("too slow")))
    .push(Router::with_path("reports").get(report));
```

- On expiry the response is **503** with `Connection: close` (not 408), unless `.error(..)` says otherwise.
- The outer timeout always wins: a 30 s timeout on the root and 300 s on `reports` stops reports after 30 s
  (verified). Give long routes their own subtree without the short timeout instead.
- Never put a timeout above WebSocket or SSE routes; it ends them.

## Concurrency and size limits

```rust
use salvo::concurrency_limiter::max_concurrency;
use salvo::size_limiter::max_size;

Router::with_path("upload").hoop(max_size(10 * 1024 * 1024)).hoop(max_concurrency(2)).post(upload);
```

`max_concurrency(n)` is a semaphore per call: it rejects (429, no queueing) when `n` requests are in flight below
it. Each call creates a separate limit, so build it once per subtree, not in a loop. `max_size` limits the request
body (413). `req.payload_with_max_size(n)` sets a limit for one read.

## Caching

`CachingHeaders::new()` adds `ETag` and `Last-Modified` and answers 304 to matching conditional requests.
`Cache` stores whole responses (GET only by default; streaming bodies and errors are never stored):

```rust
use std::time::Duration;
use salvo::cache::{Cache, MokaStore, RequestIssuer};

let cache = Cache::new(
    MokaStore::builder().time_to_live(Duration::from_secs(60)).max_capacity(10_000).build(),
    RequestIssuer::default(),      // key: scheme, authority, path, query, method (each toggleable)
);
```

The key type of `MokaStore` is inferred from the issuer; a `let store = MokaStore::builder()...` on its own line
needs `MokaStore::<String>::builder()`. Per-user data needs a custom issuer:
`|req: &mut Request, depot: &Depot| -> Option<String>` (returning `None` skips caching). `.skipper(..)` changes
which requests are cached. For caching values rather than responses use `moka::future::Cache` behind
`affix_state`.

## Logging and tracing

```rust
tracing_subscriber::fmt()
    .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")))
    .init();
let service = Service::new(router).hoop(Logger::new());
```

`Logger` emits a span per request (remote address, version, method, path) and logs status and duration. Put it on
the `Service`: on a router it misses unmatched paths and logs before the catcher has set the final status.
`Logger::new().log_status_error(false)` stops it logging `StatusError` bodies. Nothing is printed without a tracing
subscriber. `RequestId` sets and propagates `x-request-id`. Feature `otel` adds `salvo::otel` (`Tracing`,
`Metrics` middleware) for OpenTelemetry; set up the exporter with the `opentelemetry` crates of the version
`salvo-otel` depends on (`cargo tree -i opentelemetry`).

Never log request bodies or `Authorization`/`Cookie` headers.