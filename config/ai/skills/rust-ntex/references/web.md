# Web framework (`ntex::web`)

Guide chapters 8.1 to 8.5 (<https://docs.ntex.rs>) and the ntex.rs docs pages (application, handlers, extractors,
requests, responses, middleware, errors, url-dispatch, testing, static-files, websockets). The canonical complete
example is `state/web-state` in <https://github.com/ntex-rs/examples>.

## Starting the server

| Call                                       | Use                                                                   |
|--------------------------------------------|-----------------------------------------------------------------------|
| `web::server(async \|_\| App::new()...)`   | no application state (`App<()>`, `DefaultError`)                      |
| `web::server_with_config(cfg, async \|st\| App::new()...)` | per-worker state from `cfg: impl ServerAppConfig`, or an `async \|\| -> io::Result<State>` closure |
| `HttpService::new(app.build())` on `ntex::server::build()` | connection-level services before HTTP (TLS, connection state), see the server reference |

`HttpServer` (returned by both) offers `workers`, `backlog`, `max_connections`, `max_tls_handshakes`,
`graceful_shutdown_timeout`, `graceful_shutdown`, `stop_on_panic`, `disable_signals`, `stop_runtime`, `affinity`,
`load_balancing`, then `bind(addr, cfg)`, `bind_rustls(addr, &rustls_config, cfg)`, `bind_openssl`, `bind_uds`,
`listen(std_listener, cfg)`, and finally `run()`, which returns an awaitable `Server` controller.

To log the address actually bound (or bind port 0), bind a `std::net::TcpListener` yourself, read
`local_addr()`, and pass it with `.listen(listener, cfg)` instead of `.bind(addr, cfg)`.

The app factory is called on every worker, and again if a worker restarts, so keep it repeatable. It returns an
application factory; `HttpService` builds a new application service (running every `Middleware::create`) for each
connection.

## Application state

```rust
#[derive(Clone)]
struct AppState {
    pool: Arc<Pool>,                    // shared by all workers: create outside the factory, clone the Arc in
    cache: Rc<RefCell<HashMap<K, V>>>,  // shared by the connections of one worker
}

impl web::State for AppState {
    type Error = web::DefaultError;     // or your own error domain
}

impl ntex::server::ServerAppConfig for AppConfig {   // Send + Sync, process-wide
    type State = AppState;
    async fn create(&self) -> std::io::Result<AppState> { /* per worker; may open connections */ }
}
```

- The state must be `Clone`; it is cloned per listener and per connection. Plain fields are copies, handles share.
- `web::AppState<T>` wraps any `T` with `DefaultError` so you need not implement `web::State`; it derefs to `T`.
- `AppServices::build_with::<Outer>(state)` embeds a fixed state in the app factory when the surrounding pipeline has
  another state type (or none). `server_with_config` is the choice when state needs async per-worker setup.
- `WebAppConfig::new().set_state(value)` (installed with `App::with_config`) is a typed store of `Send + Sync`
  configuration, one value per type, read with `HttpRequest::app_state::<T>()` or `WebRequest::app_state()`. Extractor
  configs (`JsonConfig`, `FormConfig`, `PayloadConfig`) live there. Without `with_config` the app uses the
  `WebAppConfig` from the connection's `SharedCfg`, then built-in defaults.

## Request-local state

Every request carries a value of type `In` in `WebRequest<In>`, starting as `()`. Filters and middleware read it with
`req.st()` / `req.st_mut()` and replace it with `req.map_state(|old| new)`, which changes the request's type. A
`to_with_state()` handler receives it as its second argument, by value. Extractors cannot see it and `to()`
handlers drop it.

Because the type changes, the compiler enforces order: after a filter producing `WebRequest<User>`, routes at that
level and below must take `User`; a handler taking `User` cannot be registered where no such filter runs
(`expected Route<AppState, Option<User>>, found Route<AppState, User>`). Modular configuration names the type:
`fn routes(cfg: &mut web::ServiceConfig<AppState, User>)`.

## Routing

```rust
App::new()
    .service(
        web::scope("/api/v1")
            .service(
                web::resource("/users/{id:[0-9]+}")
                    .name("user")
                    .route(web::get().to_with_state(get_user))
                    .route(web::delete().to_with_state(delete_user))
                    .default_service(web::to(method_not_allowed)),
            )
            .configure(more_routes)
            .default_service(web::to(not_found)),
    )
    .route("/health", web::get().to(async || "ok"))
    .default_service(web::to(not_found))
```

- Builder order: `with_config`, `middleware`, `filter`, `case_insensitive_routing`, `external_resource` first; the
  first `route`/`service`/`configure`/`default_service` switches to service registration (`AppServices`), where
  only `route`, `service`, `default_service` remain. `Scope` and `Resource` follow the same rule for their settings
  (`guard`, `middleware`, `filter`, `case_insensitive_routing`).
- Request flow: app middleware, app filters, app router (resource or scope by path + resource guards), scope
  middleware and filters, scope router, resource middleware and filters, route guards in registration order, handler.
  Responses travel back through middleware only.
- Fallbacks: no path match at app level runs the app default (built-in 404, empty body). A matched scope with no
  matching service runs the scope default (built-in 404), never the app's. A matched resource with no matching route
  runs the resource default (built-in 405).
- `App::route(path, web::get().to(h))` creates a resource whose method guard is a resource guard: a wrong method
  continues routing and usually ends in 404. Use `web::resource(path).route(...)` for 405 behaviour. `Scope::route`
  behaves like `App::route`.
- `Resource::to(h)` matches every method; register it after guarded routes.
- `Route::method` adds to a list, so `web::get().method(Method::HEAD)` answers both, and ntex leaves the body out of
  the HEAD response (verified over HTTP). For a 405 with an `Allow` header, give the resource a `default_service`
  that sets it.
- Method helpers: `get post put patch delete head query`, `web::method(Method::OPTIONS)` for others, `web::route()`
  with `.method()` and `.guard(guard::Header("content-type", "application/json"))`. Guards see only the head.
- Attribute macros: `#[web::get("/path")] async fn h() -> ... {}` then `App::service(h)`.
- Patterns: `{name}` one non-empty segment, `{name:regex}` whole-segment regex, `{name}*` remainder including `/`
  (not percent-decoded, may be empty), arrays for aliases `web::resource(["/health", "/status"])`. Invalid patterns
  panic when the app is built. Scope prefixes match whole segments; inside `scope("/api")`, `""` is `/api` and `"/"`
  is `/api/`.
- URL generation: `req.url_for("user", ["42"])?` (scope variables first, values percent-encoded, returns
  `ntex::url::Url`, errors as `UrlGenerationError`), `url_for_static`, `App::external_resource(name, template)`.
  Scheme and host come from `ConnectionInfo` (`Forwarded`, `X-Forwarded-*`, `Host`); strip untrusted forwarding
  headers at the proxy.

## Handlers

- `to(h)`: `async fn h(extractor1, extractor2, ...) -> impl Responder`, up to 16 extractors.
- `to_with_state(h)`: `async fn h(st: &St, req_state: In, extractors...) -> impl Responder<St>`.
- Closures work: `web::get().to(async || "ok")`, `web::post().to(async |body: String| body)`.
- `impl Responder` means `impl Responder<()>`, which only fits `App<()>`. With application state write
  `impl Responder<AppState>`.
- A `to_with_state` handler returning `impl Trait` must also add `+ use<>`:
  `async fn h(st: &AppState, _: ()) -> Result<impl Responder<AppState> + use<>, ApiError>`.

## Extractors (`ntex::web::types`)

| Extractor                    | Reads                                                         | Default limit |
|------------------------------|---------------------------------------------------------------|---------------|
| `HttpRequest`                | the request handle (headers, `match_info()`, `peer_addr()`, `connection_info()`) | |
| `Path<T>`                    | path variables; tuple by position, `Deserialize` struct by name | |
| `Query<T>`                   | query string into a struct or map (not a tuple)               |               |
| `Json<T>`                    | JSON body (`application/json` and `+json` types)              | 32 KiB        |
| `Form<T>`                    | `application/x-www-form-urlencoded` body                      | 16 KiB        |
| `Bytes`, `String`            | whole body (`String` decodes the charset)                     | 256 KiB       |
| `Payload`                    | the body as a stream, no limit, not decompressed              |               |
| `Option<T>`, `Result<T, T::Error>` | turn an extractor failure into `None` / `Err` instead of a response | |
| `(A, B, ...)`                | several extractors as one argument                            |               |

- A body is read once; use one body extractor per handler.
- Limits: `WebAppConfig::new().set_state(JsonConfig::default().limit(4096))`, `FormConfig`, `PayloadConfig::new(n)`
  (can also require a content type for `Bytes`/`String`).
- Custom extractor:

```rust
impl<St: web::State> FromRequest<St> for ClientName {
    type Error = web::InternalError<&'static str>;

    async fn from_request(_: &St, req: &HttpRequest, _: &mut ntex::http::Payload) -> Result<Self, Self::Error> {
        req.headers().get("x-client-name").and_then(|v| v.to_str().ok()).map(|v| ClientName(v.to_owned()))
            .ok_or_else(|| web::error::ErrorBadRequest("missing x-client-name"))
    }
}
```

## Responders

| Return type                              | Response                                                |
|------------------------------------------|---------------------------------------------------------|
| `HttpResponse`, `HttpResponseBuilder`    | as built                                                |
| `String`, `&'static str`                 | 200 `text/plain; charset=utf-8`                         |
| `Bytes`, `BytesMut`, `&'static [u8]`     | 200 `application/octet-stream`                          |
| `Json<T>` / `Form<T>`                    | 200 JSON / urlencoded                                   |
| `Option<T>`                              | `None` is 404                                           |
| `Result<T, E>`                           | `E: WebResponseError<St, St::Error>` renders the error  |
| `(T, StatusCode)`, `Either<A, B>`        | changed status / either branch (`ntex::util::Either`)  |
| `()`                                     | empty 200, only for `()` state                          |

- `Json(v).with_status(StatusCode::CREATED).with_header("location", url)` (trait `web::Responder`).
- Builders: `HttpResponse::Ok().content_type("text/plain").body("x")`, `.json(&value)`, `.build()` (no body),
  `HttpResponse::builder(StatusCode::IM_A_TEAPOT)`, `.header(name, value)`. Finishing a builder twice panics.
- Custom: `impl<St: web::State> Responder<St> for T` with
  `async fn respond_to(self, st: &St, req: &HttpRequest) -> Response`.
- Streaming bodies: `HttpResponse::Ok().streaming(stream)` with `Stream<Item = Result<Bytes, E>>`.

## Filters

```rust
// with state: register with fn_service_st(f); without: .filter(async |req: WebRequest<()>| Ok::<_, Infallible>(req))
async fn authenticate(st: &AppState, req: WebRequest<()>) -> Result<WebRequest<Option<User>>, Infallible> {
    let user = req.headers().get(header::AUTHORIZATION)
        .and_then(|v| v.to_str().ok()).and_then(|v| v.strip_prefix("Bearer "))
        .and_then(|token| st.users.get(token)).cloned();
    Ok(req.map_state(|()| user))
}

async fn require_admin(_: &AppState, req: WebRequest<Option<User>>) -> Result<WebRequest<User>, AuthError> {
    match req.st() {
        None => Err(AuthError::Unauthorized),
        Some(u) if !u.admin => Err(AuthError::Forbidden),
        Some(_) => Ok(req.map_state(|u| u.unwrap())),
    }
}

App::new()
    .filter(fn_service_st(authenticate))
    .route("/", web::get().to_with_state(index))            // index(&AppState, Option<User>)
    .service(web::scope("/admin").filter(fn_service_st(require_admin))
        .route("/users", web::get().to_with_state(users)))  // users(&AppState, User)
```

Filters run in registration order, on the way in only, after all middleware of the same level. They cannot produce a
successful response; an `Err` (any `WebResponseError` for the domain) stops processing and is rendered outside the
middleware stack. Use middleware when the response matters or a request must be answered early.

## Middleware

```rust
struct Timing;
struct TimingService<S> { service: S }   // named field: forward_ready!/forward_shutdown! need it

impl<S> Middleware<S, AppState> for Timing {
    type Service = TimingService<S>;
    fn create(&self, _st: &AppState, service: S) -> Self::Service { TimingService { service } } // per connection
}

impl<S, R> Service<AppState, WebRequest<R>> for TimingService<S>
where
    S: Service<AppState, WebRequest<R>, Res = WebResponse>,
{
    type Res = WebResponse;
    type Error = S::Error;

    ntex::forward_ready!(AppState, service);
    ntex::forward_shutdown!(AppState, service);

    async fn call(&self, req: WebRequest<R>, ctx: Ctx<'_, Self, AppState>) -> Result<WebResponse, S::Error> {
        let start = std::time::Instant::now();
        let mut res = ctx.call(&self.service, req).await?;   // never self.service.call(...)
        let elapsed = HeaderValue::from(start.elapsed().as_micros() as u64);
        res.headers_mut().insert(HeaderName::from_static("x-elapsed-us"), elapsed);
        Ok(res)
    }
}
```

- Generic over every state: `impl<S, St> Middleware<S, St> for M` and `impl<S, St, R> Service<St, WebRequest<R>>`.
- App state per request: `ctx.st()`. To keep something from it, clone an owned handle in `create()`.
- Short-circuit without calling the inner service: `return Ok(req.into_response(HttpResponse::Forbidden()));`
  (`into_response` takes anything `Into<Response>`, builders included).
- Order: the first registered middleware is outermost (receives the request first, the response last). All
  middleware of a level wraps that level's filters and router.
- Built-in: `middleware::Logger::default()` (writes through `log`; format `%a %t "%r" %s %b "%{Referer}i"
  "%{User-Agent}i" %T`), `middleware::DefaultHeaders::new().header(k, v)` (only when absent), `middleware::Compress`
  (`compress` feature). That is the whole built-in set in ntex 4: rewrite error responses with your own middleware
  that inspects `res.status()` (website `examples/middleware/src/errorhandler.rs`). Separate crates, all
  runtime-neutral (no tokio in their tree, so fine on compio): `ntex-cors`, `ntex-session` (cookie sessions),
  `ntex-identity`, `ntex-files` (static files, reads on the blocking pool), `ntex-multipart`.
- `ntex-cors` 4.1 (ntex-extras repo), checked against tower-http's behaviour:

  ```rust
  let cors = Cors::new()
      .allowed_methods([Method::GET, Method::POST, Method::PATCH, Method::DELETE])
      .allowed_header(header::CONTENT_TYPE);
  let cors = if any { cors.send_wildcard() }                   // "*": answer with `*`, not the echoed origin
             else { origins.iter().fold(cors, |c, o| c.allowed_origin(o)) };
  App::new().middleware(cors.build())                          // CorsMiddleware<St>, holds an Rc: build per worker
  ```

  - A request whose `Origin` is not allowed is refused with `CorsError::OriginNotAllowed`, rendered through the error
    domain (400 by default), where tower-http serves it without the CORS header. Requests without `Origin` pass. With
    a custom domain, `impl WebResponseError<AppState, MyError> for ntex_cors::CorsError` or the `middleware()` call
    does not compile.
  - Preflights are answered by the middleware itself, before routing, so they never reach a 404/405.
  - `allowed_origin` keeps an unparsable origin as an error and `build()` panics on it, which happens inside the
    worker's app factory. `ntex::url::Url` (urly) accepts relative strings such as `not an origin`, so validate at
    startup yourself: parse with `Url::try_from` and require `scheme()` and `host()`.
  - Register it inside the middleware that renders errors, so error responses get the CORS headers too.

## Errors

```rust
#[derive(Debug, thiserror::Error)]
enum ApiError { #[error("not found")] NotFound }

impl web::State for AppState { type Error = ApiError; }  // the domain

impl WebResponseError<AppState, ApiError> for ApiError {
    fn error_response(&self, _st: &AppState) -> HttpResponse {
        HttpResponse::NotFound().content_type("application/problem+json").json(&problem(self))
    }
}
impl WebResponseError<AppState, ApiError> for web::error::JsonPayloadError { /* Overflow 413, ContentType 415 */ }
impl WebResponseError<AppState, ApiError> for web::error::PathError { /* 404 */ }
impl WebResponseError<AppState, ApiError> for serde_json::Error { /* Json<T> responder failed: 500 */ }
```

- `DefaultError` renders plain text: malformed query, JSON, form or content type 400; JSON/form over limit 413;
  form `Content-Length` 411; path deserialize 404; response serialize 500; `Bytes`/`String` errors 400 (even over the
  limit); `BlockingError` and `io::Error` 500.
- A custom domain replaces all of that. Map each error the app can produce: handler errors, extractor errors
  (`JsonPayloadError`, `PathError`, `QueryPayloadError`, `UrlencodedError` for `Form`, `PayloadError` for
  `Bytes`/`String`, `UrlGenerationError` if `url_for` errors propagate, `BlockingError<E>` from `web::block`,
  `io::Error`), responder errors (`serde_json::Error`, `serde_urlencoded::ser::Error`), and filter and middleware
  errors. The orphan rule allows these impls because `AppState` and the domain type are local.
- Domain-independent errors: `InternalError<T>` (all `web::error::Error*` helpers, 39 of them, and
  `InternalError::from_response(cause, response)`), `WebError`, `Either<A, B>`, `Infallible`.
- `error_response` receives the state, not the request, so an RFC 9457 `instance` (request path) is not available
  there. To fill it in anyway: have the renderer store the problem in the response
  (`response.extensions_mut().insert(problem)`; `extensions_mut` takes `&self`), and add an outer middleware that
  takes it back out (`res.response().extensions_mut().remove::<Problem>()`), splits the response with
  `res.into_parts()`, renders the problem again with `instance: Some(req.path().to_owned())`, copies the other
  headers over (CORS, `Allow`) and returns `WebResponse::new(response, req)`. It also gives bodiless 4xx/5xx a body.
  Errors that travel as `Err` (filters, middleware) bypass it.
- `JsonPayloadError::Deserialize(e)` covers both malformed JSON and JSON of the wrong shape. `e.is_data()` tells
  them apart, for axum's split: syntax 400, wrong shape 422 (`Overflow` 413, `ContentType` 415).
- Where they travel: extractor, handler and responder errors become `WebResponse`s inside the route, so response
  middleware sees them. Filter and middleware errors are `WebError`s that unwind through middleware as `Err` and are
  rendered by the outer app service; a middleware using `?` never sees that rendered response.

## Testing (`ntex::web::test`)

```rust
#[ntex::test]
async fn creates() {
    // without state: test::init_service(App::new()...)
    let app = test::init_service_st(AppState::new(..), my_crate::app()).await;
    let req = test::TestRequest::post().uri("/api/v1/todos").json(&json!({ "title": "x" })).to_request();
    let res = test::call_service(&app, req).await;
    assert_eq!(res.status(), StatusCode::CREATED);
    let body = test::read_body(res).await;                  // or test::read_response_json::<T>(&app, req)
}

#[ntex::test]
async fn over_http() {
    // test::server wants a factory for `()` state; map_state_factory supplies the app's state.
    let srv = test::server(async |_| ntex::service::map_state_factory(AppState::new(..), my_crate::app()));
    let res = srv.post("/api/v1/todos").send_json(&json!({ "title": "x" })).await.unwrap();
    let todo: serde_json::Value = res.json().await.unwrap();
}
```

- `TestRequest`: `get/post/put/patch/delete()`, `default().method(m)`, `.uri()`, `.header()`, `.json()`, `.form()`,
  `.payload()`, `.cookie()`, `.peer_addr()`, `.app_state(v)` (WebAppConfig value); `to_request()` for a service,
  `to_http_request()` to call a handler or `test::from_request`/`test::respond_to` directly, `to_srv_request()` for a
  middleware or filter.
- `test::server_with(test::config().h1() / .h2() / .rustls(cfg), factory)` for protocol-specific servers;
  `srv.ws_at("/ws")` opens a WebSocket client.
- Test method mismatches, trailing slashes, regex constraints, scope boundaries and every fallback: each exercises
  a different part of routing. Assert the rendered status, headers and body, not the Rust error value.

## Static files, WebSockets, HTTP/2

- `ntex-files`: `Files::new("/static", "./static").index_file("index.html")` registered with `App::service`;
  `show_files_listing()` is opt-in. `NamedFile::open(path)` for a single file; never build the path from an
  unvalidated `{tail}*`.
- WebSockets (`ws` feature, default): in a handler, `web::ws::start(&req, subprotocol, service).await` performs the
  handshake and runs a `Service<ws::WsSink, ws::Frame, Res = Option<ws::Message>>` until close; the handler returns
  `()`. `ctx.st()` in that service is the `WsSink` for sending at any time. Ping/pong and heartbeats are yours to
  handle (website `examples/websockets`).
- HTTP/2 is negotiated through ALPN with `bind_rustls`/`bind_openssl`. Cleartext h2 needs prior knowledge and the
  lower-level `HttpService`; the HTTP/1.1 `Upgrade: h2c` path is not supported.