---
name: rust-actix
description: "Build Rust HTTP services with actix-web 4 on its tokio-based runtime: HttpServer with one single-threaded runtime per worker and an App factory, App/scope/resource/route routing with guards, {id} and {tail:.*} path patterns, async handlers whose arguments are extractors (Path, Query, Json, Form, Data, ThinData, HttpRequest, Bytes, Payload) and whose return types are Responders (HttpResponse builder, Json, Redirect, Either, customize()), JsonConfig/PathConfig/QueryConfig error handlers, ResponseError and RFC 9457 problem details, middleware with from_fn, wrap_fn or Transform + Service and their order, built-in Logger, DefaultHeaders, NormalizePath, ErrorHandlers, Condition and Compress, actix-cors, web::Data shared state, web::block for blocking work, databases (sqlx, Diesel, SeaORM), graceful shutdown and signals, TLS and HTTP/2, actix-ws WebSockets, actix-files, and actix_web::test (init_service, TestRequest, call_and_read_body_json). Use when learning actix-web, starting an actix project, or writing, reviewing or debugging code that imports `actix_web`, `actix_cors`, `actix_files`, `actix_ws` or `actix_rt` (App, HttpServer, web::resource, web::scope, HttpResponse, Responder, FromRequest, ResponseError, ServiceRequest, Next), including routes that answer 404 or 405 unexpectedly, extractor errors in the wrong format, middleware that does not compile, and app factories with lifetime errors."
license: MIT
---

# actix-web 4

actix-web is a tokio-based framework built from `Service`s. `HttpServer::new(factory)` starts one worker thread per
CPU, each running its own single-threaded tokio runtime (actix-rt), and calls `factory` on every worker to build
that worker's `App`. Anything built inside the factory is per worker; anything that must be shared is made once
outside it and cloned in, usually as `web::Data<T>` (an `Arc`). Handlers are plain `async fn`s: each argument is an
extractor (`FromRequest`), the return value a `Responder`.

Written against actix-web 4.18.1 (actix-http 3.20, actix-router 0.5, actix-server 2.10, actix-rt 2.15), actix-cors
0.7.2, MSRV 1.89, on Rust 1.99 with edition 2024. Behaviour claims marked in the references were run against these
versions. Check `Cargo.lock` and confirm anything surprising against the sources below.

## Where to look

| Need                              | Source                                                                                        |
|-----------------------------------|-----------------------------------------------------------------------------------------------|
| Guide                             | <https://actix.rs/docs>, sources in `docs/` of <https://github.com/actix/actix-website>; its snippets are in `examples/` there |
| API reference                     | <https://docs.rs/actix-web/4>; `actix_web::middleware` and `actix_web::test` module docs       |
| Runnable apps                     | <https://github.com/actix/examples> (databases, auth, CORS, WebSockets, TLS, ...)             |
| CORS, sessions, identity, limits  | <https://github.com/actix/actix-extras> (`actix-cors`, `actix-session`, `actix-identity`, ...) |
| Files and WebSockets              | `actix-files` and `actix-ws` on docs.rs                                                       |
| What changed per release          | `CHANGES.md` in each crate of <https://github.com/actix/actix-web>                             |

Read the reference that matches the task before writing code:

- [references/app-and-routing.md](references/app-and-routing.md): `App`, scopes, resources, routes, guards, path
  patterns, default services, `configure`, `url_for`, path normalisation, application state.
- [references/handlers.md](references/handlers.md): handler signatures, extractors and their configs, custom
  extractors, responders, streaming.
- [references/errors.md](references/errors.md): `ResponseError`, actix's `Error`, the default extractor failures,
  error handlers, RFC 9457 problem details with `instance`, `ErrorHandlers`, panics.
- [references/middleware.md](references/middleware.md): order, `from_fn`, `wrap_fn`, `Transform` + `Service`, the
  built-ins, actix-cors.
- [references/server.md](references/server.md): `HttpServer` settings, workers and runtimes, blocking work,
  databases, signals and graceful shutdown, TLS and HTTP/2, containers.
- [references/testing.md](references/testing.md): `actix_web::test` in memory, a real server on a random port, unit
  tests of handlers and extractors.
- [references/realtime-and-files.md](references/realtime-and-files.md): `actix-ws`, server-sent events and streaming
  bodies, `actix-files`.
- [references/pitfalls.md](references/pitfalls.md): compile errors and runtime surprises already hit, with fixes.

`assets/template/` is a complete, tested todo JSON API: an `app()` factory shared by `main` and the tests, a scope
with resources, a shared store in `web::Data`, extractor configs whose error handlers produce RFC 9457 problems, an
`ApiError: ResponseError`, a `from_fn` middleware that adds `instance` and turns actix's own 404/405 and CORS
refusals into problems, actix-cors, request logging, graceful shutdown, and in-memory tests. Copy it to start a
project, and read it instead of writing these pieces from memory.

## Start a project

```toml
[dependencies]
# No default features: they add brotli/gzip/zstd compression (C code), cookies, HTTP/2, the `compat` aliases and
# unicode routing. Add back what you use: "compress-gzip", "cookies", "http2", "rustls-0_23", "openssl", ...
actix-web = { version = "4.18", default-features = false, features = ["macros"] }
serde = { version = "1", features = ["derive"] }
```

`macros` gives `#[actix_web::main]`, `#[actix_web::test]` and the routing macros (`#[get("/")]`). HTTP/2 needs
`http2` (plus a TLS feature, or `bind_auto_h2c` for cleartext). Without `unicode`, custom segment regexes
(`{id:\d+}`) use `regex-lite`, whose classes such as `\d` and `\w` are ASCII-only.

## Shape of an app

```rust
use actix_web::{App, HttpResponse, HttpServer, web};

async fn hello(name: web::Path<String>) -> String {
    format!("Hello, {name}")
}

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    let counter = web::Data::new(std::sync::atomic::AtomicU64::new(0)); // shared by every worker
    HttpServer::new(move || {
        App::new()
            .app_data(counter.clone())
            .route("/hello/{name}", web::get().to(hello))
            .route("/health", web::get().to(|| async { HttpResponse::Ok().body("Healthy") }))
    })
    .bind(("0.0.0.0", 8080))?
    .run()
    .await
}
```

For a production shape (one `app()` function used by `main` and the tests, problem details, CORS, logging,
shutdown) see `assets/template/src/lib.rs` and `src/main.rs`.

## Rules that prevent most mistakes

State and workers:

- The factory runs once per worker. `web::Data::new(..)` inside it makes one value per worker; create shared state
  outside and `clone()` the `Data` in. `Data<T>` is extracted by exact type: registering `Data<Arc<T>>` and
  extracting `Data<T>` is a 500 at request time ("Requested application data is not configured correctly").
- Handlers run on single-threaded runtimes. A blocking call (Diesel, `std::fs` loops, CPU work, `std::thread::sleep`)
  stalls every request on that worker: use `web::block(..)` or an async driver. Never hold a `std::sync::Mutex`
  guard across `.await`.

Routing:

- `web::get()` matches GET only. A HEAD request to a GET-only resource answers 405. Register
  `web::route().guard(guard::Any(guard::Get()).or(guard::Head()))` when HEAD must work; actix-http leaves the body
  out of HEAD responses.
- Patterns match the path exactly: `/todos/` does not match `/todos`, and `//todos` matches nothing. Add
  `middleware::NormalizePath::trim()` only if you want both forms (it also merges `//`).
- A resource whose routes all fail their guards answers 405 with an `Allow` header built from the method guards it
  checked, and an empty body. An unmatched path reaches the app's (or scope's) `default_service`: 404, empty body.
- `App::route`, `App::service(web::resource(..))` and `web::scope(..)` are matched in registration order. A scope
  matches by prefix, so register more specific scopes first.

Extractors and errors:

- Without configured error handlers, extractor failures are `text/plain` messages: every `Json` failure is 400
  (wrong content type included), a `Path` value that does not parse is **404**, `Query` is 400. Register
  `JsonConfig`, `PathConfig` and `QueryConfig` with `error_handler(..)` through `app_data` for JSON errors.
- Only one extractor may read the body (`Json`, `Form`, `Bytes`, `String`, `Payload`). Put it last among the
  arguments so cheaper extractors fail first.
- Handler errors implement `ResponseError`; `error_response()` cannot see the request. Add request details
  (`instance`) in a middleware, as the template does. `actix_web::error::ErrorBadRequest(e)` and friends write `e`'s
  `Display` into the body: never wrap internal errors in them.

Middleware:

- `wrap` order is reversed: the **last** `.wrap(..)` is the outermost and sees the request first.
- `middleware::from_fn(async fn(req: ServiceRequest, next: Next<impl MessageBody>) -> Result<ServiceResponse<impl
  MessageBody>, Error>)` is the easiest way to write one. Changing the body type means returning
  `ServiceResponse<BoxBody>` (`res.map_into_boxed_body()`).
- Do not keep a clone of the `HttpRequest` (`req.request().clone()`) alive across `next.call(req)`: routing needs
  the only reference and panics in `Rc::get_mut` otherwise. Read what you need first, or take it from the
  response afterwards (`res.request()`).
- actix-cors refuses `allowed_origin("*")` (use `allow_any_origin()`), and an invalid origin does not panic: every
  worker fails to build its app, nothing answers, and `run()` ends with "Can not start server service 0". Validate
  configured origins before building `Cors`.

Server and tests:

- `HttpServer::new(..).bind(..)?.run().await`. Defaults: workers = `available_parallelism()`, SIGTERM stops
  gracefully within `shutdown_timeout` (30 s), SIGINT and SIGQUIT stop at once, keep-alive 5 s.
- A factory returning `App<impl ServiceFactory<..>>` that takes a reference argument fails in edition 2024
  ("lifetime may not live long enough" in the factory closure): take owned arguments.
- Test the whole app in memory with `test::init_service(app(..))` and `TestRequest`, under `#[actix_web::test]`.
  actix's `HeaderMap` has no `Index` impl: use `headers().get(NAME)`.

## Working method

1. Check versions and features: `actix-web` in `Cargo.lock`, and that every item used has its feature on (an
   unresolved `middleware::Compress` means no `compress-*` feature; `cookie` items need `cookies`).
2. Start from `assets/template/` or the closest app in actix/examples, and copy its shape instead of inventing an
   API.
3. `cargo build`, then `cargo clippy --all-targets`. Long `ServiceFactory`/`Transform` bound errors usually mean a
   middleware's body type does not match; references/pitfalls.md maps messages to fixes.
4. `cargo test`, then run it and curl the routes, including HEAD, wrong methods, unknown paths, trailing slashes,
   malformed and oversized bodies, path values of the wrong type, and a CORS preflight.

## Learning path

When someone is learning actix-web rather than shipping a change, go in this order. After each step have them change
the template so the idea is used, not only read.

1. Getting started and Application (actix.rs), then run `assets/template` and curl it. Exercise: add
   `GET /api/v1/todos/count`.
2. Handlers and Extractors (references/handlers.md). Exercise: a `Form` endpoint and a `Path<(u64, String)>` route,
   then a custom extractor that reads an `x-api-key` header.
3. URL Dispatch (references/app-and-routing.md). Exercise: a guard-based route, a nested scope with its own default
   service, `url_for` in a `Location` header.
4. State and the server (references/server.md). Exercise: a per-worker counter next to a shared one; watch them
   differ with `workers(4)`; move a blocking call into `web::block`.
5. Errors (references/errors.md). Exercise: a 409 problem for a duplicate title, then see which errors reach the
   `problem_details` middleware and why.
6. Middleware (references/middleware.md). Exercise: rewrite `log_request` as `Transform` + `Service`, then add
   `DefaultHeaders` and a `Condition`-wrapped `Compress`.
7. Testing (references/testing.md). Exercise: a test for every error added above, and one real-HTTP test with a
   random port.
8. WebSockets, streaming and files (references/realtime-and-files.md) as the project needs them.
