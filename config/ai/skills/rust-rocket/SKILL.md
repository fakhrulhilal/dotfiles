---
name: rust-rocket
description: "Build Rust HTTP services with Rocket 0.5 on tokio: #[get]/#[post]/#[patch]/#[delete] route attributes, mount and routes!, <param>/<path..> segments and FromParam, ?<query> parameters and FromForm/FromFormField, data guards (Json, Form, Data, limits), request guards (FromRequest, &State, Option/Result), ranking and forwarding, Responders (Json, status::Created, Redirect, (ContentType, T), custom Responder), catchers (#[catch(404)], #[catch(default)]) and RFC 9457 problem details, 405 with Allow (which Rocket lacks), managed state and sentinels, fairings (on_ignite, on_liftoff, on_request, on_response, AdHoc) for opening databases, CORS and request logging, request-local cache, rocket::Config and figment (rocket::custom, limits, shutdown grace/mercy, ident, log_level), logging with tracing, rocket_db_pools and when not to use it, Shield, launch errors, and the local test client (rocket::local::asynchronous::Client). Use when learning Rocket, starting a Rocket project, or writing, reviewing or debugging code that imports `rocket` (rocket::build, rocket::custom, Rocket<Build>, routes!, catchers!, State, Responder, FromData, FromRequest, Fairing, AdHoc, LocalResponse), including routes that answer 404 or 422 unexpectedly, errors that are HTML instead of JSON, missing 405s, duplicated or missing logs, and launch failures."
license: MIT
---

# Rocket 0.5

Rocket is a tokio-based framework built around attributes and guards. A route is an `async fn` with a method
attribute; each argument is a *guard* that Rocket evaluates before calling it (path and query parameters, request
guards, at most one data guard for the body). A guard that fails runs a *catcher* for its status; one that forwards
lets the next matching route try. Cross-cutting work goes in *fairings* (callbacks at ignite, liftoff, request,
response, shutdown); shared values in *managed state*. Configuration is a figment.

Written against Rocket 0.5.2 (rocket_http 0.5.2, rocket_codegen 0.5.2), on Rust 1.99 with edition 2024. Behaviour
claims marked *(run)* in the references were run against it; others were read in its source. Check `Cargo.lock`
and confirm anything surprising against the sources below.

## Where to look

| Need                     | Source                                                                                 |
|--------------------------|----------------------------------------------------------------------------------------|
| Guide                    | <https://rocket.rs/guide/v0.5/> (requests, responses, state, fairings, configuration, testing) |
| API reference            | <https://api.rocket.rs/v0.5/rocket/>; `rocket::local::asynchronous`, `rocket::fairing`, `rocket::serde::json` |
| Runnable examples        | `examples/` in <https://github.com/rwf2/Rocket> (branch `v0.5`)                       |
| Databases                | <https://api.rocket.rs/v0.5/rocket_db_pools/>                                         |
| Source, when docs are thin | `~/.cargo/registry/src/*/rocket-0.5.2/src` (`serde/json.rs`, `server.rs`, `error.rs`, `config/`) |

Read the reference that matches the task before writing code:

- [references/routing-and-requests.md](references/routing-and-requests.md): routes, mounting, ranks, path matching,
  the missing 405, path and query parameters, body guards and limits, request guards, HEAD.
- [references/responses-and-errors.md](references/responses-and-errors.md): responders, catchers, getting a guard's
  error to a catcher, RFC 9457 problem details for every error.
- [references/fairings-and-state.md](references/fairings-and-state.md): managed state and sentinels, fairings,
  request-local cache, opening a database in an ignite fairing, `rocket_db_pools`, CORS.
- [references/config-and-server.md](references/config-and-server.md): `rocket::Config` and figment, logging with
  tracing, launch errors, shutdown, runtime, containers.
- [references/testing.md](references/testing.md): the local client, unit tests, a real server on a random port.
- [references/pitfalls.md](references/pitfalls.md): compile errors and runtime surprises already hit, with fixes.

`assets/template/` is a complete, tested todo JSON API: `rocket()` shared by `main` and the tests, Rocket's config
built in code (2 MiB JSON limit, no `Server` header, graceful shutdown, Rocket's logger off), a store opened by an
ignite fairing and passed as `&State`, `ApiError`/`Problem` responders, a JSON data guard with 415, catchers that
render guard failures as problems, 405 routes with `Allow`, a CORS fairing, a request-log fairing, and local-client
tests. Copy it to start a project, and read it instead of writing these pieces from memory. todo-app's
`backend/rocket/` is the same shape with configuration from YAML/env/CLI, sqlx and Turso stores, health checks and
an OpenAPI document from utoipa.

## Start a project

```toml
[dependencies]
# json: Json guard and responder. uuid: Uuid as a path/form parameter. Others: secrets (private cookies), tls, mtls.
rocket = { version = "0.5.2", features = ["json", "uuid"] }
serde = { version = "1", features = ["derive"] }
```

`http2` is on by default. Serde derives through Rocket's re-export need `#[serde(crate = "rocket::serde")]`; a direct
`serde` dependency avoids that.

## Shape of an app

```rust
use rocket::{State, serde::json::Json};

#[rocket::get("/hello/<name>")]
fn hello(name: &str) -> String {
    format!("Hello, {name}")
}

#[rocket::get("/count")]
fn count(hits: &State<std::sync::atomic::AtomicU64>) -> Json<u64> {
    Json(hits.fetch_add(1, std::sync::atomic::Ordering::Relaxed))
}

#[rocket::launch]
fn rocket() -> _ {
    rocket::build()
        .manage(std::sync::atomic::AtomicU64::new(0))
        .mount("/", rocket::routes![hello, count])
}
```

For a production shape (config in code, problems, 405, CORS, logging, tests) see `assets/template/src/lib.rs`.

## Rules that prevent most mistakes

Routing:

- A route matches one method. **Rocket has no 405**: another method on a known path is a 404 unless you add 405
  routes (`method_not_allowed` in the template). Leave HEAD out of them; GET routes answer HEAD.
- Paths match by non-empty segments: `/todos/` and `//todos` reach `/todos` *(run)*.
- Same method + path + rank collide and the launch fails. Give catch-alls a high rank.

Guards:

- A path or query value that does not parse **forwards with 422**, and the 422 catcher runs without the error. Take
  `Result<T, E>` for a path value and the text (`Option<String>`) for a query value, and answer 400 yourself.
- Rocket's `Json<T>` does not check `Content-Type`; `format = "json"` forwards instead of answering 415. Wrap it in a
  data guard that checks first. Its own statuses are 400 (syntax), 422 (shape), 413 (over `limits.json`, 1 MiB by
  default).
- Catchers get `(Status, &Request)`, never a guard's error. Put the error in `request.local_cache(..)` (keyed by a
  type of your own) and read it in `#[catch(default)]`.

Responses and errors:

- `Result<T, ApiError>` where `ApiError: Responder` is the handler error pattern. A responder sees the request, so
  set request-dependent fields (`instance`) there.
- Register catchers for 404 and `default`, or errors are Rocket's HTML/JSON pages. A panic in a handler is a 500
  through the catchers.
- A `Status` responder only accepts 100, 200 to 205 and errors; a 3xx is turned into a 500.

State, fairings, config:

- `&State<T>` without `.manage(T)` aborts the launch (sentinel). Open databases in `AdHoc::try_on_ignite` and
  `manage` them; log the cause there, since the launch error only names the fairing.
- `on_request` cannot answer. Rewrite in `on_response` (CORS preflight, error bodies). `on_response` callbacks run in
  attach order: attach the request log last.
- Build `rocket::Config { .., ..Default::default() }` and pass it to `rocket::custom` to configure Rocket from your
  own settings; `rocket::build()` reads `Rocket.toml` and `ROCKET_*`. Nest app keys under `app` (top-level `env`/`log`
  warn as deprecated). `address` is an `IpAddr`.
- With tracing, Rocket's `log` output arrives through the bridge even with `log_level: Off`: filter `rocket=off`.
- A `rocket::Error` dropped unread panics: match `error.kind()` for the message.

## Working method

1. Check `rocket` in `Cargo.lock` and its features (`json`, `uuid`, `secrets`, `tls`).
2. Start from `assets/template/` or the closest example in rwf2/Rocket's `examples/`, and copy its shape.
3. `cargo build`, then `cargo clippy --all-targets`. Codegen errors point at the attribute; read the guard trait it
   names (`FromParam`, `FromFormField`, `FromRequest`, `FromData`, `Responder`).
4. `cargo test` with the local client, then run it and curl: HEAD, wrong methods (405 + `Allow`), unknown paths,
   trailing slashes, malformed, wrong-shaped, oversized and non-JSON bodies, bad path and query values, a CORS
   preflight, and SIGTERM while a request is open.

## Learning path

When someone is learning Rocket rather than shipping a change, go in this order. After each step have them change
the template so the idea is used, not only read.

1. Overview and Requests in the guide, then run `assets/template` and curl it. Exercise: add
   `GET /api/v1/todos/count`.
2. Guards (references/routing-and-requests.md). Exercise: a request guard that reads `x-api-key` and fails with 401;
   watch the default catcher render it; then leave a detail in the local cache.
3. Responses (references/responses-and-errors.md). Exercise: a 409 problem for a duplicate title, and a custom
   `Responder` that adds an `ETag`.
4. State and fairings (references/fairings-and-state.md). Exercise: a hit counter in managed state; an
   `AdHoc::on_liftoff` that logs the bound port; an `on_response` fairing that adds `X-Request-Id`.
5. Configuration (references/config-and-server.md). Exercise: change limits and shutdown in code, then read an app
   key back with `extract_inner`.
6. Testing (references/testing.md). Exercise: a test for every error added above.
7. Databases as the project needs them: `rocket_db_pools` if its sqlx version fits, otherwise an ignite fairing.
