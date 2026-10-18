---
name: rust-salvo
description: "Build Rust HTTP services with Salvo 1.0 on tokio: #[handler] functions, Router trees with with_path/push/hoop, {id}/{id:num}/{**rest} path syntax, middleware as handlers that call ctrl.call_next, Depot and affix_state for shared state, extractors (JsonBody, PathParam, QueryParam, Extractible), Writer/Scribe responses, StatusError, RFC 9457 Problem and catchers, OpenAPI with #[endpoint], ToSchema, ToParameters and Swagger UI, built-in middleware (CORS, JWT and basic auth, sessions, CSRF, flash, rate and concurrency limits, timeout, compression, caching, logging, request id), WebSocket, SSE, static files, uploads and NamedFile downloads, TLS, ACME, HTTP/3, reverse proxy, graceful shutdown and TestClient tests. Use when learning Salvo, starting a Salvo project, or writing, reviewing or debugging code that imports `salvo` (Router, Service, Server, TcpListener, Depot, FlowCtrl, #[handler], #[endpoint], salvo::oapi), including handler or Writer impls that do not compile and routes that do not match."
license: Apache-2.0
---

# Salvo 1.0

Salvo is a tokio web framework where everything is a `Handler`: endpoints, middleware, the proxy, static file
serving and the error catcher. A `Router` is a tree of filters (path, method, custom) with handlers attached; a
`Service` wraps the root router with service-wide middleware and a `Catcher`; a `Server` serves it on an
acceptor (`TcpListener`, TLS, QUIC, joined listeners).

Written against salvo 1.0.1 (Rust 1.94 or newer, edition 2024). Every API shown here was compiled, and every
behaviour claim was run, against 1.0.1. Check `Cargo.lock` and confirm anything surprising against the sources
below.

## Where to look

| Need                            | Source                                                                                  |
|---------------------------------|-----------------------------------------------------------------------------------------|
| Guide and concepts              | <https://salvo.rs> (book), module docs on <https://docs.rs/salvo>                       |
| API reference                   | <https://docs.rs/salvo/1.0.1>; per crate `salvo_core`, `salvo_extra`, `salvo-oapi`, ... |
| Runnable examples               | `examples/` in <https://github.com/salvo-rs/salvo> (one crate per feature)              |
| What a feature flag pulls in    | the `[features]` table of `salvo`'s `Cargo.toml`                                        |
| The source this skill grew from | <https://github.com/salvo-rs/salvo-skills> (29 small skills, several wrong for 1.0.1)   |

Read the reference that matches the task before writing code:

- [references/routing.md](references/routing.md): path syntax and wildcards, methods, nesting, filters, middleware
  scope, redirects.
- [references/handlers.md](references/handlers.md): handler signatures, responses, reading the request, extractors,
  `Extractible`, validation, `Depot`.
- [references/errors.md](references/errors.md): `StatusError`, custom `Writer` errors, RFC 9457 `Problem`, catchers,
  panics, `anyhow`.
- [references/middleware.md](references/middleware.md): writing middleware, `FlowCtrl`, order, and the built-ins
  (compression, timeout, concurrency and size limits, caching, logging, request id).
- [references/security.md](references/security.md): CORS, JWT and basic auth, sessions, CSRF, rate limiting, flash
  messages, security headers.
- [references/openapi.md](references/openapi.md): `#[endpoint]`, extractors and schemas, endpoint attributes,
  document metadata, security schemes, documenting custom errors, doc UIs.
- [references/realtime.md](references/realtime.md): WebSocket and Server-Sent Events, fan-out, heartbeats.
- [references/files.md](references/files.md): `StaticDir`, `StaticFile`, embedded assets, uploads, `NamedFile`
  downloads.
- [references/server.md](references/server.md): server and listeners, graceful shutdown, TLS, ACME, HTTP/3, reverse
  proxy.
- [references/database.md](references/database.md): sharing a pool through `affix_state`, SQLx, SeaORM, Diesel.
- [references/testing.md](references/testing.md): `TestClient` and `ResponseExt`.
- [references/pitfalls.md](references/pitfalls.md): compile errors and runtime surprises already hit, including the
  upstream skill mistakes, with fixes.

`assets/template/` is a complete, tested todo JSON API: `#[endpoint]` handlers under `/api/v1`, state through
`affix_state::inject`, an `ApiError` that renders RFC 9457 problem details and documents itself in OpenAPI, a catcher
that turns every other error (unknown path, wrong method, bad JSON, bad path value, panic) into problem details,
Swagger UI, a health probe, graceful shutdown on Ctrl+C and SIGTERM, and `TestClient` tests. Copy it to start a
project and read it instead of writing these pieces from memory.

## Start a project

```toml
[dependencies]
salvo = { version = "1.0.1", features = ["oapi", "rfc9457", "logging", "catch-panic", "affix-state"] }
tokio = { version = "1", features = ["macros", "rt-multi-thread", "signal"] }
serde = { version = "1", features = ["derive"] }
```

Default features are `server`, `server-handle` (graceful shutdown), `http1`, `http2`, `cookie`, `test`
(`TestClient`), `aws-lc-rs`, `matched-path` and `fix-http1-request-uri`. Everything else is opt-in by name: `oapi`,
`rfc9457`, `cors`, `jwt-auth`, `basic-auth`, `session`, `csrf`, `flash`, `rate-limiter`, `concurrency-limiter`,
`size-limiter`, `timeout`, `compression`, `cache`, `caching-headers`, `logging`, `request-id`, `catch-panic`,
`affix-state`, `serve-static`, `websocket`, `sse`, `proxy`, `rustls`, `native-tls`, `openssl`, `quinn`, `acme`,
`force-https`, `trailing-slash`, `tower-compat`, `anyhow`, `eyre`, `otel`. `full` enables all of them except
`size-limiter` (and `eyre`); prefer listing the features you use, since `full` also builds every TLS stack.

## Shape of an app

```rust
use salvo::prelude::*;

#[handler]
async fn hello(req: &mut Request) -> String {
    let name = req.param::<String>("name").unwrap_or_default();
    format!("Hello, {name}")
}

#[tokio::main]
async fn main() {
    let router = Router::new()
        .push(Router::with_path("hello/{name}").get(hello))
        .push(Router::with_path("health").get(health));
    let acceptor = TcpListener::new("0.0.0.0:8080").bind().await;
    Server::new(acceptor).serve(router).await;
}

#[handler]
async fn health() -> &'static str {
    "Healthy"
}
```

For production wrap the router in a `Service` (service-wide `Logger`, a `Catcher`), and add graceful shutdown; see
`assets/template/src/lib.rs` and `src/main.rs`.

## Rules that prevent most mistakes

Handlers:

- `#[handler]` (and `#[endpoint]`) turns the function into a **unit struct with the function's name**. A closure
  argument, variable or parameter with that name then fails ("cannot be named the same as a unit struct"): a handler
  named `ws` breaks `|ws| ...` inside it. Give handlers verb names (`ws_echo`, `list_todos`).
- Handler parameters are any of `&mut Request`, `&mut Depot`, `&mut Response`, `&mut FlowCtrl` and extractors, in
  any order. The return type implements `Writer` (anything `Scribe`, `Json<T>`, `Text<T>`, `StatusCode`, `String`,
  `&'static str`, `()`, `Result<T, E>` with both sides `Writer`).
- `impl Writer for X` and `impl Handler for X` need `#[async_trait]` (re-exported in the prelude). The newer traits
  (`BasicAuthValidator`, `RateIssuer`, `QuotaGetter`) use native `async fn` and must **not** have it.
- `res.status_code` is a public field (`Option<StatusCode>`) for reading; `res.status_code(code)` is the setter.
- `depot.get::<T>(key)` and `depot.get_typed::<T>()` return `Result`, not `Option`. `obtain`/`inject` are
  deprecated aliases.

Routing:

- Path syntax is `{name}` (the pre-0.76 `<name>` is treated as a literal). `{id:num}`, `{id|regex}` and
  `{id:num(..=10)}` constrain a segment and answer 404 when it does not match. Wildcards: `{**rest}` zero or more
  segments, `{*+rest}` one or more, `{*?rest}` zero or one. A single segment is just `{name}`. Details and the
  verified matching table: references/routing.md.
- Path values are percent-decoded: `/files/a%2Fb` gives `{name}` the value `a/b`. Validate before using a value as a
  file name.
- Trailing and doubled slashes still match (`/todos/`, `//todos`). A GET route does not answer HEAD (405).
- A path that matches with the wrong method answers 405; an unknown path answers 404. Both, and every error with an
  empty body, go through the `Catcher`.
- `hoop()` middleware on a router runs only when a route below it matches. Middleware that must see every request
  (logging, 404s) goes on the `Service`: `Service::new(router).hoop(Logger::new())`.

Middleware and errors:

- Middleware is a handler that calls `ctrl.call_next(req, depot, res).await`; code after it runs on the way out.
  Rendering a 4xx/5xx or 3xx status stops the chain automatically; call `ctrl.skip_rest()` anyway for clarity.
- Return `Result<T, StatusError>` or a custom error that implements `Writer` (and `EndpointOutRegister` under
  `#[endpoint]`). Clients always see `StatusError::brief` (`detail` and `cause` only if `SALVO_STATUS_ERROR` allows);
  never put internal error text in `brief`.
- The default catcher negotiates HTML/JSON/XML/text. For APIs use the `rfc9457` feature: `Catcher::new(ProblemGoal)`
  or the template's `problem::catch` (which also sets `instance`).
- Extractor failures (malformed JSON, a JSON body sent as `text/plain`, `PathParam<u64>` given `abc`) answer 400 with
  the detail `parse http data failed`.

OpenAPI:

- Only `#[endpoint]` handlers are documented, and only what the macro can see: extractor parameters and the return
  type. `req.param()`/`req.query()` inside the body are invisible.
- Every type in an endpoint's signature needs `ToSchema`/`ToParameters`, and the error type needs
  `EndpointOutRegister`, or the endpoint does not compile.
- Build the document from the router (`OpenApi::new(..).merge_router(&router)`) **before** `unshift`-ing the doc and
  UI routes onto it, so they are not documented themselves.

Server:

- `Server::handle()` before `serve()` gives a `ServerHandle`; spawn a task that waits for Ctrl+C and SIGTERM and calls
  `stop_graceful(Duration)`. Without it SIGTERM kills in-flight requests.
- Never block the runtime: wrap Diesel, `std::fs` loops and CPU work in `tokio::task::spawn_blocking`.
- Do not hold a `std::sync::Mutex` guard or a `RwLock` write guard across `.await`.

## Working method

1. Check versions and features: `salvo` in `Cargo.lock`, and that every module you import has its feature enabled
   (`could not find X in salvo` with a note "gated behind the `x` feature" means a missing feature).
2. Start from `assets/template/` or the closest example in the salvo repo, and copy its shape instead of inventing an
   API. The upstream salvo-skills snippets are not reliable for 1.0.1; references/pitfalls.md lists what is wrong.
3. `cargo build`, then `cargo clippy --all-targets`. Map unfamiliar trait errors with references/pitfalls.md.
4. `cargo test` with `TestClient`, then run it and curl the routes, including wrong methods, unknown paths, malformed
   and oversized bodies, and non-matching path values.

## Learning path

When someone is learning Salvo rather than shipping a change, go in this order. After each step have them change the
template so the idea is used, not only read.

1. Handlers and routers (salvo.rs "Getting started", references/handlers.md and routing.md). Exercise: add
   `GET /api/v1/todos/count`, then a nested `todos/{id}/notes` router.
2. Middleware and the depot (references/middleware.md). Exercise: a timing middleware that logs the duration after
   `call_next`, and one that rejects requests without an `x-api-key` header, scoped to `/api`.
3. Errors and catchers (references/errors.md). Exercise: return a 409 problem when a title already exists, and see
   which errors reach the catcher.
4. OpenAPI (references/openapi.md). Exercise: add a `ToParameters` query struct for paging and check it in Swagger UI.
5. Testing (references/testing.md). Exercise: a test for every error path added above.
6. Security and limits (references/security.md). Exercise: JWT on the write routes, CORS for one origin, a rate limit
   on `POST`.
7. Real-time, files and operations (references/realtime.md, files.md, server.md) as the project needs them.