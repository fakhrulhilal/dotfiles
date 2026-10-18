# Pitfalls

Each entry was reproduced against salvo 1.0.1 (macOS, Rust 1.99, edition 2024).

## Compile errors

| Message (abridged)                                                                                                                                        | Cause and fix                                                                                                                                                          |
|-----------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `function parameters cannot shadow unit structs` / `` `ws` is interpreted as a unit struct ``                                                             | `#[handler] async fn ws(..)` defines a unit struct `ws`; a closure parameter or binding `ws` inside the module now refers to it. Rename the handler (`ws_echo`).       |
| `lifetime parameters or bounds on method write do not match the trait declaration`                                                                        | `impl Writer for X` (or `impl Handler`) without `#[async_trait]`. Add it; it is in the prelude.                                                                        |
| the same message on `impl BasicAuthValidator` / `RateIssuer` / `QuotaGetter` with `#[async_trait]`                                                        | These traits use native `async fn`; remove `#[async_trait]`.                                                                                                           |
| `the trait bound X: Clone is not satisfied` at `.hoop(auth)` for a `JwtAuth<X, _>`                                                                        | JWT claims must be `Clone` (plus `DeserializeOwned`). Derive it.                                                                                                       |
| `the trait bound ApiError: EndpointOutRegister is not satisfied`, followed by `mismatched types` and `the method write exists for struct ..` at the macro | An `#[endpoint]` returns a custom error. Implement `EndpointOutRegister` (openapi.md) or use `#[handler]`; the follow-on errors disappear with the first.              |
| `the trait bound X: ToSchema is not satisfied` (or `ToParameters`) under `#[endpoint]`                                                                    | Derive them on every body, `Json` payload and parameter struct.                                                                                                        |
| `unexpected token, expected type` at `request_body(content = "multipart/form-data")`                                                                      | `content` is a type. Use `request_body(content = Form, content_type = "multipart/form-data")`.                                                                         |
| `no method named and found for MethodFilter`                                                                                                              | `use salvo::routing::Filter;`                                                                                                                                          |
| `no associated function named see_other found for Redirect`                                                                                               | It is `Redirect::other` (303).                                                                                                                                         |
| `the trait From<Vec<&str>> is not implemented for AllowMethods`                                                                                           | Pass `Method` values: `[Method::GET, Method::POST]`.                                                                                                                   |
| `no method named with_skipper found for RateLimiter`                                                                                                      | It is `.skipper(..)`.                                                                                                                                                  |
| `type annotations needed for MokaStore<_, _>`                                                                                                             | A limiter or cache built in a standalone `let _ = ..` has nothing to infer its key from. Pass it straight to `hoop(..)`, or annotate `MokaStore::<String>::builder()`. |
| `could not find size_limiter in salvo` (gated behind `size-limiter`)                                                                                      | `full` does not include `size-limiter`; add it. Any "gated behind the feature" note means a missing feature.                                                           |
| `could not find acme in conn`                                                                                                                             | It is `salvo::acme::{LETS_ENCRYPT_STAGING, KeyType, ..}`.                                                                                                              |
| `no method named to_str found for FlashLevel`                                                                                                             | It is `as_str()` (or `Display`).                                                                                                                                       |
| `no method named clear found for Session`                                                                                                                 | Use `remove(key)`, `destroy()` or `regenerate()`.                                                                                                                      |
| `no method named is_err found for ()` after `NamedFile::builder(..).send(..).await`                                                                       | `send` returns `()` and renders 404/500 itself. Use `.build().await` for a `Result`.                                                                                   |
| `no variant V3_2 found for OpenApiVersion`                                                                                                                | `OpenApiVersion::Version3_2`.                                                                                                                                          |
| `method takes 0 generic arguments` on `depot.remove::<T>(key)`                                                                                            | `remove(key)` is untyped (`Option<Box<dyn Any>>`); `remove_typed::<T>()` is typed.                                                                                     |
| warning `use of deprecated method Depot::obtain` / `inject`                                                                                               | `get_typed` / `insert_typed`.                                                                                                                                          |
| `cannot move out of type [Holding]`                                                                                                                       | `holdings()[0].local_addr.clone()`; it is Salvo's `SocketAddr`, `.into_std()` converts.                                                                                |

## Runtime surprises

- **`{*name}` matches several segments**: it behaves like `{**name}`. One segment is `{name}`; one or more is
  `{*+name}`; zero or one is `{*?name}` (routing.md has the verified table).
- **`%2F` arrives as `/`** in a path parameter. Validate decoded values before using them as paths.
- **GET does not answer HEAD**: 405. Load balancers that probe with HEAD need an explicit `.head(..)`.
- **Router hoops do not run for unmatched paths**: a 404 skips them. Logging, CORS and anything that must see every
  request go on the `Service`.
- **Errors render as HTML by default**: an API client without `Accept: application/json` gets an HTML error page.
  Use `Catcher::new(ProblemGoal)` (`rfc9457`) or the template's `problem::catch`.
- **`ProblemGoal` has no `instance`**: use the template's catcher when clients rely on it.
- **Extractor failures say only `parse http data failed`** (400), including a JSON body sent as `text/plain`.
- **`StatusError` detail and cause are hidden** by default (`SALVO_STATUS_ERROR=never_detail,never_cause`); `brief`
  is always shown. Keep internal error text out of `brief`.
- **A panic drops the connection** unless `CatchPanic` is hooped; the server keeps running.
- **An outer `Timeout` wins over a longer inner one**: the shorter, outer one fires first (upstream claims the
  opposite). Timeouts answer 503, not 408.
- **`JwtAuth` answers 403, not 401, for an invalid or expired token**; 401 only when no token was found.
- **`RemoteIpIssuer` answers 400 under `TestClient`** (no peer address).
- **`max_concurrency` rejects with 429 instead of queueing**; `max_size` answers 413.
- **`Cors::into_handler()` panics** on an invalid origin, or on credentials combined with `Any`.
- **`SessionHandler::builder(..)` panics on a secret shorter than 64 bytes**; `HandlerBuilder::try_new` returns
  `Result`.
- **`TcpListener::bind()` panics when the port is taken** (`bind failed`); `try_bind()` returns `Result`.
- **SVG files download instead of displaying** from `StaticDir`: XML types default to `attachment`; use
  `.disposition_type("inline")` for trusted assets.
- **Component names in OpenAPI are full Rust paths** (`my_crate.todos.Todo`) until a short-mode `FlexNamer` is set.
- **`status_codes(201, ..)` drops the 200 that `Json<T>` registered**, leaving no success response. Add
  `responses((status_code = 201, body = T))`.
- **`take_string()` twice**: the second call returns `""`.

## Upstream salvo-skills errors

The vendored source (`salvo-rs/salvo-skills` at `68b0c6d`, written for 1.0.0) is wrong for 1.0.1 in these places;
this skill uses the corrected forms above: `{*}` described as one segment; `Redirect::see_other`; filters `.and()`
without the trait import; `Cors::allow_methods(vec!["GET", ..])`; `RateLimiter::with_skipper`; `impl Writer`
without `#[async_trait]` (openapi skill); JWT claims without `Clone`; `jsonwebtoken = "9"` (salvo-jwt-auth uses 11);
`request_body(content = "multipart/form-data")`; `NamedFile..send(..).await.is_err()`; `salvo::conn::acme`;
`FlashLevel::to_str` (and "`as_str` does not exist", which is backwards); `Session::clear`; the claim that a longer
inner timeout overrides an outer one; the claim that the proxy's reqwest client needs a separate feature; and a
realtime quick start whose `async fn ws` collides with its `|mut ws|` closure.