# Pitfalls

Each entry was reproduced against ntex 4.3.0 with the `compio` feature (macOS, Rust 1.99, edition 2024) unless it
says otherwise.

## Compile errors

| Message (abridged)                                                                    | Cause and fix |
|---------------------------------------------------------------------------------------|---------------|
| `the trait HandlerSt<_, _, _> is not implemented for fn item ... {create}` on `to_with_state(create)` | The handler returns `impl Trait`, which in edition 2024 captures the `&AppState` lifetime. Write `-> Result<impl Responder<AppState> + use<>, ApiError>`, or return a concrete type. Also check that the second argument's type equals the request state at that point (the filters before the route). |
| `expected Route<AppState, Option<User>>, found Route<AppState, User>`                 | A handler requires a request state no filter before it produces. Register it inside the scope or resource whose filter produces that type. |
| `expected Route<AppState, ...>, found Route<(), _>`                                    | A `to()` handler returns `impl Responder`, which is `Responder<()>`. Write `impl Responder<AppState>`. |
| `the trait bound X: WebResponseError<AppState, MyError> is not satisfied` (often `JsonError` = `serde_json::Error`, `JsonPayloadError`, `PathError`) | A custom error domain has no renderer for an error some extractor, handler or responder produces. Add `impl WebResponseError<AppState, MyError> for X`. |
| `type mismatch resolving <AppState as State>::Error == DefaultError`                  | A responder or helper is only implemented for `DefaultError`, or `with_status`/`with_header` were called on a responder whose state parameter defaulted to `()`. Use method syntax with `use ntex::web::Responder;` and an `impl Responder<AppState>` return type. |
| `no method named middleware/filter/with_config ... found for AppServices`              | A setting came after the first `route`/`service`/`configure`/`default_service`. Move it up. |
| `cannot infer type of the type parameter St/In declared on the struct App`            | A standalone `App::new()` (tests, helpers) has nothing to infer the state from. Write `App::<()>::new()`, `App::<AppState>::new()` or `App::default()`. Inside a server factory it is inferred. |
| `queries overflow the depth limit!` / `reached the recursion limit`                   | Deep service type stacks, typically in a test that passes an app to `web::test::server`. Add `#![recursion_limit = "256"]` at the top of that crate root (`lib.rs`, `main.rs` or the test file); several upstream examples carry it. |
| `no method named and_then found for FnServiceSt`                                       | `and_then` on a plain service is a `Service` trait method: `use ntex::Service;`, or start the chain with `ntex::service(f)`. |
| `no method named finish found for HttpResponseBuilder`                                 | Finish a builder with `.build()` (no body), `.body(x)`, `.json(&x)` or `.streaming(s)`. |
| `no function or associated item named build found for HttpResponse`                    | Use `HttpResponse::builder(status)` (or `HttpResponseBuilder::new(status)`). |
| `Configuration is not implemented for Result<ClientConfig, HttpError>`                 | `ClientConfig::set_header`, `set_basic_auth` and `set_bearer_auth` return `Result`. Add `?`. |
| `lifetime may not live long enough ... return type of async closure ... contains a lifetime` on `async move \|_: &AppState\| app(&cfg)` | `fn app(cfg: &X) -> impl ServiceFactory<...>` captures the borrow in edition 2024, though the factory keeps nothing borrowed. End the return type with `+ use<>`. |
| `cannot index into a value of type &ntex::http::HeaderMap`                             | `headers()[NAME]` does not exist in ntex. Use `headers().get(NAME)`, or `ClientResponse::header(NAME)` in tests. |
| `the trait bound CorsError: WebResponseError<AppState, MyError> is not satisfied`      | `ntex-cors` renders its errors through the app's domain. Add the impl (a 400 problem). |

## Runtime surprises

- **CORS answers 400 where tower-http did not**: `ntex-cors` refuses a request from an origin it does not allow,
  instead of serving it without `Access-Control-Allow-Origin`. Expected; see web.md.
- **A bad CORS origin panics a worker at start**: `Cors::build()` panics on an origin `allowed_origin` could not
  parse, inside the app factory. Validate origins before starting the server (web.md).

- **A tokio crate panics on a worker**: `there is no reactor running, must be called from the context of a Tokio 1.x
  runtime`. Inside `ntex::rt::spawn` the task ends in `JoinError` and the panic is only logged. The compio backend
  has no tokio reactor; see "tokio-only crates" in runtime-compio.md.
- **Every worker panics at start in a Linux container**: io_uring is blocked (Docker's default seccomp profile) and
  compio was built without the `polling` fallback. Enable `compio/polling` (runtime-compio.md). Not reproduced here
  (macOS), derived from the compio-driver build configuration.
- **tokio backend selected by accident**: some dependency enables `ntex/tokio`; tokio wins over compio. The log line
  `Starting compio runtime` is missing. Check `cargo tree -e features -i ntex`.
- **Unknown path or wrong method answers with an empty body**: the built-in fallbacks are a bare 404/405. Add
  `default_service` on the app, on every scope, and on resources whose 405 must carry a body.
- **Wrong method gives 404 instead of 405**: the route was registered with `App::route`/`Scope::route`. Group the
  path's methods in `web::resource(path)`.
- **"Configuration "IO Configuration" does not exist, using default" logged per worker at INFO**: the `SharedCfg`
  passed to `bind()` lacks `IoConfig`/`HttpServiceConfig` (and `WebAppConfig` when the app has no `with_config`).
  Add them, or set `ntex_service::cfg=warn` in the log filter.
- **Expensive setup repeated for every connection**: `Middleware::create()` runs once per connection (measured: three
  `Connection: close` requests on one worker, three calls; the `state/web-state` example's comment saying "once per
  worker" is wrong). Do the work in the state factory and clone a handle in `create()`.
- **Counters or caches differ between requests**: they live in worker state (`Rc`), and requests land on different
  workers. Process-wide data must be created outside the state factory and shared as `Arc`.
- **A field change in state is invisible to other connections**: state is cloned per connection, plain fields are
  copies. Put shared mutable data behind `Rc<Cell/RefCell>` or `Arc<Mutex/atomic>`.
- **`already borrowed: BorrowMutError` under load**: a `RefCell` borrow is held across `.await` and another request on
  the same worker borrows it. Scope the borrow so it ends before the `.await`.
- **Middleware does not see an error response**: errors returned by filters, middleware or custom services travel as
  `Err` through `ctx.call(..).await?` and are rendered outside the middleware stack. Handler, extractor and responder
  errors are already responses and do pass through.
- **A request id or header set by middleware is missing on a filter rejection**: same cause; catch the `Err` in the
  middleware and render it there if the header must always appear.
- **`/api` gives 404 while `/api/users` works**: inside `scope("/api")`, `""` matches `/api` and `"/"` matches `/api/`.
- **Remainder path contains `%20`**: `{tail}*` values are not percent-decoded; decode and validate before use.
- **Graceful stop cuts connections**: SIGINT/Ctrl-C stops immediately by design; send SIGTERM (container stop does)
  and leave `graceful_shutdown_timeout` above each connection's `IoConfig::set_shutdown_timeout`.

## Documentation drift

docs.ntex.rs and the ntex repository's `main` branch run ahead of crates.io. In October 2026 crates.io had ntex
4.3.0, ntex-server 4.2.0 and ntex-rt 4.2.1, while the docs described `Server::metrics()`, `affinity()`,
`load_balancing()`, `latency_interval()`, runtime busy-time metrics and connection close reasons, none of which those
releases contain. Before using an API from the guide, confirm it on <https://docs.rs/ntex> for your version or in
`~/.cargo/registry/src/*/ntex-*/src`. The ntex.rs website examples use `features = ["tokio"]`; drop it for compio.