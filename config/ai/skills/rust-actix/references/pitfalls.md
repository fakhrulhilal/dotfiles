# Pitfalls

Each entry was reproduced against actix-web 4.18.1, actix-cors 0.7.2 and actix-files 0.7.3 (macOS, Rust 1.99,
edition 2024), most of them while porting a JSON API with RFC 9457 errors from axum to actix.

## Compile errors

| Message (abridged)                                                                                       | Cause and fix                                                                                                                       |
|----------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| `lifetime may not live long enough ... return type of closure App<impl ServiceFactory<..>> contains a lifetime` at `HttpServer::new(move \|\| app(state.clone(), &settings))` | Edition 2024: the `impl` return type of `app()` captures the borrowed argument. Take it by value (`settings.clone()`). `+ use<>` does not fix it: the nested `impl MessageBody` captures it too ("impl Trait captures lifetime parameter, but it is not mentioned in use<...>"). |
| `the trait bound X: Responder is not satisfied` at `web::to(handler)`                                    | The handler returns a type that is only a `ResponseError` (or `Result<_, E>` where `E` is not `Into<Error>`). Return `HttpResponse` (`err.error_response()`) or `Result<R, E>`. |
| `cannot index into a value of type &HeaderMap`                                                           | actix's `HeaderMap` (not `http`'s) has no `Index`. Use `headers().get(name)`.                                                     |
| `no associated function named open_async found for NamedFile`                                            | Not in actix-files 0.7.3. Use `NamedFile::open` (or inside `web::block`).                                                           |
| `the trait bound AppState: FromRequest is not satisfied` at `.to(handler)`                              | A handler argument that is not an extractor: `AppState` or `&AppState` instead of `web::Data<AppState>`. The error points at the route, not the argument. |
| `mismatched types` at `next.call(req).await` in a `from_fn` middleware that also returns early         | The early `req.into_response(..)` and `next.call(..)` have different body types behind one `impl MessageBody`. Box both (`map_into_boxed_body()`, return `ServiceResponse<BoxBody>`) or use `EitherBody` with `map_into_left_body()` / `map_into_right_body()`. |
| `non-exhaustive patterns: _ not covered` matching `JsonPayloadError`, `PathError`, `QueryPayloadError` | They are `#[non_exhaustive]`: add a catch-all arm.                                                                                   |
| unresolved `actix_web::middleware::Compress`                                                             | No `compress-*` feature (default features off).                                                                                    |

## Runtime surprises

- **A clone of the request kept across `next.call` panics the router.** `let r = req.request().clone();` before
  `next.call(req)` makes every request panic at `actix-web/src/request.rs` (`Rc::get_mut(..).unwrap()` in
  `match_info_mut`), dropping the connection. Copy what you need first, or read `res.request()` afterwards.
- **`web::get()` does not answer HEAD.** A HEAD request gets 405. Use
  `web::route().guard(guard::Any(guard::Get()).or(guard::Head()))`; actix-http then omits the body.
- **A bad path value is 404, not 400.** `web::Path<u32>` given `abc` answers 404 `can not parse "abc" to a u32`.
  Set `PathConfig::error_handler` for 400.
- **A JSON body with the wrong content type is 400, not 415**, and every default extractor error is `text/plain`.
  Configure `JsonConfig::error_handler` (errors.md).
- **No path normalisation.** `/todos/` and `//todos` are 404 for a `/todos` resource. `NormalizePath::trim()` makes
  them match (and also merges `//` everywhere).
- **Built-in 404 and 405 have empty bodies** (405 with `Allow: GET, POST`, comma-space separated). Turn them into
  JSON in `default_service`s or a response-rewriting middleware.
- **`allowed_origin("*")` or a malformed origin in actix-cors**: no panic, but every worker fails to build the app,
  connections close without a response, and `run()` ends with `Can not start server service 0`. The reason is only
  an ERROR line from `actix_cors::builder`. Use `allow_any_origin()` and validate origins first.
- **actix-cors refuses a bad preflight with 400** where tower-http answers 200 without the allow headers, and lists
  `Access-Control-Allow-Methods` in hash order. Browsers treat both the same.
- **A panic in a handler drops the connection** with no response; there is no catch-panic middleware.
- **Missing app data is a 500 at request time**, not a startup error: `Data<T>` registered as `Data<Arc<T>>`, or
  registered in another scope.
- **State built inside the `HttpServer::new` closure is per worker.** Counters and caches differ between workers;
  build shared state outside and clone the `Data` in.
- **`#[actix_web::main]` logs "Actix runtime found; starting in Actix runtime"** at info through `log`: it is normal.
- **The `Logger` middleware and actix's own logs use the `log` crate.** With tracing, `tracing-subscriber`'s
  `try_init()` installs the `log` bridge (default `tracing-log` feature); a subscriber built without it shows none
  of them.
