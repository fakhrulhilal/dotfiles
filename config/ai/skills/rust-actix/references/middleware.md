# Middleware

From the actix.rs pages Middleware and CORS, the `actix_web::middleware` docs (and its `authors-guide.md`), and
actix-cors 0.7.2. Behaviour marked *(run)* was checked against actix-web 4.18.1.

## Order

`App::wrap`, `Scope::wrap` and `Resource::wrap` add middleware around everything registered on that level. The
**last** `wrap` is the outermost: it sees the request first and the response last.

```rust
App::new()
    .wrap(cors)           // 3rd in, 1st out
    .wrap(problem)        // 2nd in, 2nd out
    .wrap(logger)         // 1st in, last out
```

App-level middleware also runs for unmatched paths (the default service). Scope- and resource-level middleware runs
only when that scope or resource matched.

## `from_fn`: an async function

```rust
use actix_web::{body::MessageBody, dev::{ServiceRequest, ServiceResponse}, middleware::{from_fn, Next}, Error};

async fn timing(req: ServiceRequest, next: Next<impl MessageBody>) -> Result<ServiceResponse<impl MessageBody>, Error> {
    let started = std::time::Instant::now();
    let res = next.call(req).await?;
    tracing::info!(status = res.status().as_u16(), ms = started.elapsed().as_millis(), "request");
    Ok(res)
}

App::new().wrap(from_fn(timing))
```

- Up to 9 extractors may come before `req` (`async fn mw(data: web::Data<X>, req: ServiceRequest, next: ..)`);
  they are extracted from the request first.
- To answer without calling the inner service, return `Ok(req.into_response(HttpResponse::Unauthorized().finish())
  .map_into_right_body())` and declare the return type `ServiceResponse<EitherBody<impl MessageBody>>`, calling
  `.map_into_left_body()` on the normal path. Or box both: `ServiceResponse<BoxBody>` with `map_into_boxed_body()`.
- To replace the body (rendering a problem), take the response apart: `let (req, res) = res.into_parts();` build a
  new `HttpResponse` and return `ServiceResponse::new(req, new_res)`.
- Values for handlers go in the request extensions: `req.extensions_mut().insert(User { .. })`, read with
  `web::ReqData<User>` or `req.extensions().get::<User>()`.
- Do not hold `req.request().clone()` across `next.call(req)`: routing needs the only reference and panics
  *(run)*. Copy what you need (`req.path().to_owned()`) or read it from `res.request()` afterwards.

## `wrap_fn`: a closure

```rust
use actix_web::dev::Service as _;
App::new().wrap_fn(|req, srv| {
    let fut = srv.call(req);
    async move {
        let mut res = fut.await?;
        res.headers_mut().insert(header::HeaderName::from_static("x-served-by"), header::HeaderValue::from_static("actix"));
        Ok(res)
    }
})
```

## `Transform` + `Service`: the full form

Needed for configurable middleware types, or to keep state per worker.

```rust
pub struct SayHi;

impl<S, B> Transform<S, ServiceRequest> for SayHi
where
    S: Service<ServiceRequest, Response = ServiceResponse<B>, Error = Error>,
    S::Future: 'static,
    B: 'static,
{
    type Response = ServiceResponse<B>;
    type Error = Error;
    type InitError = ();
    type Transform = SayHiMiddleware<S>;
    type Future = std::future::Ready<Result<Self::Transform, Self::InitError>>;

    fn new_transform(&self, service: S) -> Self::Future {
        std::future::ready(Ok(SayHiMiddleware { service }))
    }
}

pub struct SayHiMiddleware<S> { service: S }

impl<S, B> Service<ServiceRequest> for SayHiMiddleware<S>
where
    S: Service<ServiceRequest, Response = ServiceResponse<B>, Error = Error>,
    S::Future: 'static,
    B: 'static,
{
    type Response = ServiceResponse<B>;
    type Error = Error;
    type Future = futures_util::future::LocalBoxFuture<'static, Result<Self::Response, Self::Error>>;

    forward_ready!(service);

    fn call(&self, req: ServiceRequest) -> Self::Future {
        let fut = self.service.call(req);
        Box::pin(async move { fut.await })
    }
}
```

- `new_transform` runs once per worker for each place the middleware is registered; returning `Err` there makes the
  worker fail (see the CORS pitfall).
- To call the inner service from inside the async block, store it as `Rc<S>` and clone the `Rc` into the block.
- `Transform` needs `futures-util` for `LocalBoxFuture` (or write `Pin<Box<dyn Future<Output = ..>>>`).

## Built-ins

| Middleware                          | Notes                                                                                   |
|-------------------------------------|-----------------------------------------------------------------------------------------|
| `Logger::default()` / `Logger::new("%a %r %s %Dms")` | Access log through the `log` crate at info. `.exclude("/health")`, `.exclude_regex(..)`, `%{x-request-id}i`. |
| `DefaultHeaders::new().add(("x-version", "1"))` | Adds headers the response does not already have.                               |
| `NormalizePath::trim()` / `::new(TrailingSlash::Always)` | Rewrites the path before routing: trims or appends the slash and merges `//` *(run)*. |
| `Compress::default()`               | Needs a `compress-*` feature. Negotiates from `Accept-Encoding`.                        |
| `ErrorHandlers::new()`              | Rewrite responses by status (errors.md).                                                 |
| `Condition::new(enabled, mw)`       | Applies `mw` only when `enabled`; both arms have the same type.                         |
| `Identity`                          | No-op, for conditional wiring by type.                                                  |
| `from_fn`, `wrap_fn`                | above                                                                                    |

From actix-extras: `actix-cors`, `actix-session`, `actix-identity`, `actix-limitation` (rate limits with Redis),
`actix-web-httpauth` (bearer/basic extractors and middleware), `actix-web-lab` (newer helpers).

## CORS with actix-cors

```rust
let cors = Cors::default()                         // restrictive: nothing allowed
    .allowed_origin("https://app.example.com")     // repeat per origin
    .allowed_methods([Method::GET, Method::POST, Method::PATCH, Method::DELETE])
    .allowed_header(header::CONTENT_TYPE)
    .max_age(3600);
App::new().wrap(cors)

Cors::default().allow_any_origin().send_wildcard()  // `*`: allow_any_origin echoes the origin unless send_wildcard
Cors::permissive()                                   // everything; development only
```

- `allowed_origin("*")` is refused. So is an origin that does not parse as a URI. Neither panics: the error is
  stored, `new_transform` fails on every worker, connections are closed without a response, and the server stops
  with "Can not start server service 0"; the cause is only an ERROR log line from `actix_cors` *(run)*. Validate
  configured origins (scheme and host) before building `Cors`.
- A request from an origin that is not allowed is served without the CORS headers (`block_on_origin_mismatch` is
  `false` by default) *(run)*. Set it to `true` to answer such requests with 400 instead.
- A preflight (`OPTIONS` with `Access-Control-Request-Method`) is answered by the middleware with 200 *(run)*; one it
  refuses (origin, method or header not allowed) gets 400 with the error text, as a response, not an `Err`.
- `allowed_methods` order is not kept: `Access-Control-Allow-Methods` comes out in hash order.
- `Vary: Origin, Access-Control-Request-Method, Access-Control-Request-Headers` is added to every response unless
  `disable_vary_header()`.
- Credentials with `allow_any_origin().send_wildcard()` is an illegal combination; it fails like a bad origin.
- Wrap CORS **inside** any middleware that rewrites error responses, so its headers are on the final response and
  the error middleware sees its refusals.
