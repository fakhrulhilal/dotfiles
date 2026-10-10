---
name: rust-backend
description: Build or extend a Rust JSON API backend on axum the standard way - OpenAPI and Swagger UI generated from the handlers (utoipa), every error as RFC 9457 problem+json, ASP.NET Core-style health checks (Healthy/Degraded/Unhealthy, detail with ?format=json), storage behind one trait with PostgreSQL (sqlx) and embedded Turso/SQLite backends chosen by the database URL, and options-pattern configuration with figment where one key names a setting in YAML, the environment (DATABASE__URL) and the command line (--database-url). Use this whenever the user starts a Rust web API, REST service or backend, adds an endpoint, resource, table, migration, setting or health check to an axum service, asks for Swagger/OpenAPI docs, problem details or consistent error responses, health or readiness probes, configuration from files, environment variables or arguments, or PostgreSQL, SQLite or Turso storage in Rust, or wants an existing axum app brought in line with this setup, even if they only mention one of these parts.
---

# Rust API backend on axum

A repeatable shape for a JSON API server. `assets/template/` is a complete,
tested reference implementation (a todo API); read the files there instead of
writing these pieces from memory. `references/pitfalls.md` lists problems
already hit once, with fixes. `references/cors.md` covers browser clients on
another origin.

## The standard

| Area | Standard |
|---|---|
| Framework | axum 0.8 on tokio, graceful shutdown on Ctrl+C and SIGTERM |
| Routes | resources under `/api/v1/...`; `/` redirects to the docs; anything else is a problem 404 |
| OpenAPI | generated from the handlers with utoipa: `#[utoipa::path]` on every handler, `OpenApiRouter` + `routes!`; document at `/api/openapi.json`, Swagger UI at `/swagger-ui` |
| Errors | every error response is RFC 9457 `application/problem+json` with `type`, `title`, `status`, `detail` and `instance` (the requested path, without the query string), including the ones axum raises itself (unknown path, wrong method, malformed body, bad path or query value) |
| Validation | 422 with an `errors` object in the ASP.NET Core `ValidationProblemDetails` shape: member name to list of messages, `{ "title": ["must not be empty"] }` |
| Health | modelled on ASP.NET Core health checks: `/health/live` (no checks) and `/health/ready` (all registered checks) answer `Healthy`, `Degraded` (both 200) or `Unhealthy` (503) as plain text, the worst of the checks; `?format=json` adds each check's result; both in the OpenAPI document |
| Storage | one `async_trait` store trait, `Arc<dyn ...>` in the app state; `PostgresStore` (sqlx) and `TursoStore` (embedded, SQLite-compatible); the database URL scheme picks one; migrations run on startup |
| Configuration | built-in YAML defaults < `config.yaml` < environment variables < command-line arguments. One name per setting, its YAML key (`database.url`); the variable is the key in upper case with each dot as `__` (`DATABASE__URL`, no prefix); the argument is the key with each dot and underscore as `-` (`--database-url`) |
| Logging | tracing; plain text locally, one JSON object per line in production; one line per request, probes at debug |
| Tests | end-to-end over real HTTP against `sqlite::memory:`, one test file per source file, asserting status codes, bodies and the problem content type; unit tests next to the code for logic the HTTP tests cannot reach (health aggregation, configuration precedence) |

Why this shape: clients and operators get the same contract from every
service. A client can parse any failure the same way because there is one
error format with no exceptions. The docs cannot drift from the code because
they are generated from it. A container orchestrator can tell "restart me"
(live) from "do not send traffic yet" (ready). And one binary runs with no
infrastructure (an embedded database file) or against PostgreSQL, with nothing
but a URL changed, so local runs, tests and production share the same code.

## Layout

```text
Cargo.toml
config/default.yaml          defaults, compiled into the binary
migrations/postgres/*.sql    one set of migrations per backend
migrations/sqlite/*.sql
src/main.rs                  parse arguments, load config, init logging, run
src/lib.rs                   app(store) -> Router, run(config), request log, shutdown
src/cli.rs                   clap arguments, mirroring Config with optional fields
src/config.rs                Config structs; load() merges defaults, file, environment, arguments
src/logging.rs               text or JSON output
src/domain.rs                API types and validation
src/api.rs                   router, OpenAPI document, Swagger UI, fallback
src/api/extract.rs           Json / Path / Query that reject with a problem
src/api/problem.rs           Problem, ApiError, the problem_details middleware
src/api/health.rs            HealthCheck, HealthChecks, live and ready probes
src/api/<resource>.rs        handlers for one resource
src/store.rs                 store trait, StoreError, connect() by URL scheme
src/store/postgres.rs        sqlx implementation
src/store/turso.rs           Turso implementation and its migration runner
tests/api/main.rs            shared test helpers (spawn_app, problem) and the mod list
tests/api/<module>.rs        end-to-end tests, one file per source file they cover
```

`lib.rs` exposes `app(store)` separately from `run(config)` so tests can start
the real router on a free port with an in-memory database.

Tests mirror the source: `tests/api/todos.rs` covers `src/api/todos.rs`,
`tests/api/problem.rs` covers `problem.rs` and `extract.rs`, `health.rs` the
probes, `openapi.rs` the document, Swagger UI and root redirect in `src/api.rs`.
They are modules of one test crate (`tests/api/main.rs` declares them), not
separate files directly under `tests/`: that way they share the helpers and
build as a single binary instead of one per file.

## Starting a new API

1. Copy `assets/template/` (including `.gitignore`) to the project directory.
   The template ships no `Cargo.lock`: the first build creates one, and the
   new project commits it, as every application should.
2. Rename: the package (`todo-api` in `Cargo.toml`, `todo_api` in `main.rs`
   and under `tests/api/`), the default database path (`data/todo.db`), and
   the API title in `src/api.rs`.
3. Replace the todo resource with the user's: `domain.rs`, both migrations,
   the store trait and both implementations, `api/todos.rs`, the tests. Follow
   "Adding a resource" below. Leave no todo code behind.
4. Keep the dependency versions of the template: they are known to build
   together. If the user wants newer ones, upgrade and run the tests; utoipa,
   utoipa-axum, utoipa-swagger-ui and axum must be compatible with each other.
5. Verify (see "Verifying"), then write a README with the URL table, an
   error example, the health check answers, the configuration table (key,
   variable, argument, default) and the database URL forms. The todo-rs style is: what
   it is, how to run it, where things are, no changelog.

When adding this setup to an existing axum service instead, bring over the
pieces in this order, each one working before the next: `api/problem.rs` and
`api/extract.rs` (switch handlers to the new extractors and `ApiError`), the
fallback and `problem_details` layer, `api/health.rs`, then utoipa
annotations and `OpenApiRouter`. Keep the service's existing routes, names,
storage and status codes unless asked to change them: clients already depend
on them. That includes a validation error the service answers with 400; give
it a problem body, do not move it to 422. Say in the report what would differ
from the standard, and leave the decision to the user.

## Adding a resource

A resource touches every layer. Skipping one compiles fine and fails later,
so go through all of them:

1. **`domain.rs`**: the resource type, a `Create...` and an `Update...` input
   (update fields are `Option`, absent means unchanged), any query struct
   (`IntoParams`), and validation functions that return a client-safe message.
   Derive `ToSchema` and add `#[schema(example = ...)]` where it helps a reader
   of the docs. Ids are UUID v7 (`Uuid::now_v7()`), timestamps come from
   `domain::now()`.
2. **Migrations**: a new numbered file in *both* `migrations/postgres/` and
   `migrations/sqlite/`. Never edit a migration that may have been applied.
   For Turso, also add the file to the `MIGRATIONS` list in `store/turso.rs`
   (sqlx picks its directory up at compile time; Turso does not).
3. **`store.rs`**: methods on the trait. "Not found" is data, not an error:
   return `Option<T>` or `bool` and let the handler decide on the 404.
4. **`store/postgres.rs` and `store/turso.rs`**: implement both. See the type
   mapping below.
5. **`api/<resource>.rs`**: handlers with `#[utoipa::path]`, a `TAG`, and a
   `router()` built with `routes!`. Mount it in `api.rs` with
   `.nest("/api/v1", ...)` and add the tag to `ApiDoc`.
6. **`tests/api/<resource>.rs`**: the lifecycle and each error case of the
   resource, declared with `mod <resource>;` in `tests/api/main.rs`. Add the
   new paths to the document test in `tests/api/openapi.rs`.

### Handlers

```rust
#[utoipa::path(
    post,
    path = "/todos",
    tag = TAG,
    request_body = CreateTodo,
    responses(
        (status = 201, description = "The created todo", body = Todo),
        (status = 400, description = "Malformed request", body = Problem, content_type = PROBLEM_JSON),
        (status = 422, description = "Validation failed", body = Problem, content_type = PROBLEM_JSON),
        (status = 500, description = "Unexpected failure", body = Problem, content_type = PROBLEM_JSON),
    )
)]
async fn create_todo(
    State(state): State<AppState>,
    Json(input): Json<CreateTodo>,
) -> Result<(StatusCode, axum::Json<Todo>), ApiError> {
    let title = validate_title(&input.title).map_err(|detail| ApiError::invalid("title", detail))?;
    let todo = Todo::new(title);
    state.store.insert(&todo).await?;
    Ok((StatusCode::CREATED, axum::Json(todo)))
}
```

- Take input with `extract::{Json, Path, Query}` from `api/extract.rs`, not
  axum's. They are the same extractors with the rejection turned into
  `ApiError`; axum's own would answer in plain text and break the "every error
  is a problem" promise. Return bodies with `axum::Json`.
- Return `Result<_, ApiError>`. Store errors convert with `?`; they are logged
  with their cause and reach the client as a 500 with a fixed, harmless detail,
  because database messages can leak schema and credentials.
- Document every status the handler can produce, with `body = Problem,
  content_type = PROBLEM_JSON` for the errors, so generated clients know the
  error format.
- Status codes: 201 for create (with the resource), 200 for read and update,
  204 for delete, 400 for unreadable input, 404 for unknown ids, 422 for input
  that was read but is invalid. Add a variant to `ApiError` when a new kind of
  failure appears (409 for a conflict, for example) instead of building a
  `Problem` inside a handler. Handlers never set `instance`: the
  `problem_details` middleware in `api/problem.rs` fills it in from the request
  for every problem, so it cannot be forgotten or get out of step with the URL.
- The doc comment's first line is the operation summary in Swagger UI; the
  rest is its description.

### The two backends

| Rust | PostgreSQL | Turso / SQLite |
|---|---|---|
| `Uuid` | `UUID`, bound directly | `TEXT`, `id.to_string()` |
| `bool` | `BOOLEAN` | `INTEGER` 0/1, `i64::from(flag)` |
| `DateTime<Utc>` | `TIMESTAMPTZ` | `TEXT`, fixed-width RFC 3339 (`timestamp()`), so text order is time order |
| `Option<T>` | bind the option | `Value::Null` or the value |
| placeholders | `$1` | `?1` |
| read a row | `#[derive(sqlx::FromRow)]` row struct | `row.get(index)`, then parse |

Both must behave identically, because tests only exercise one of them: same
ordering (add a tie-breaker such as `ORDER BY created_at, id`), same result
for a missing id, same stored precision (`domain::now()` truncates to
microseconds, which is what PostgreSQL keeps, so a value read back equals the
value written). Values that fail to parse when read from Turso are
`StoreError::Corrupt`, not a panic.

## Configuration

Every setting has one name: its key in the YAML file. The other two spellings
are derived, never invented, so anyone who knows one can write the others:

| YAML key | Environment variable | Argument |
|---|---|---|
| `env` | `ENV` | `--env` |
| `server.port` | `SERVER__PORT` | `--server-port` |
| `database.url` | `DATABASE__URL` | `--database-url` |
| `database.max_connections` | `DATABASE__MAX_CONNECTIONS` | `--database-max-connections` |

- Variable: upper case, each dot becomes `__` (a single `_` would be
  ambiguous with keys like `max_connections`). No application prefix.
- Argument: each dot and underscore becomes `-`.
- Precedence: defaults, then the YAML file, then the variable, then the
  argument.

This is .NET's options pattern with [figment](https://docs.rs/figment):
`Config::load` merges the sources in order, the later one winning, and binds
the result to the config structs, which are what the application reads.
Fields are plain types (`pub port: u16`), not `Option`: the built-in defaults
guarantee a value, so nothing downstream has to unwrap.

To add a setting, touch three places:

1. the field in its config struct (`config.rs`);
2. its default in `config/default.yaml`, which is also what makes it a known
   key, so the environment variable works from here on with no more code;
3. an `Option` field in the matching `...Args` struct in `cli.rs`, with
   `#[arg(long = "section-key")]` and `skip_serializing_if`, for the argument.

The test `every_setting_has_an_argument_named_after_its_key` fails when a
default has no argument or an argument has no default. Do not write a
configuration loader by hand, and do not read `std::env::var` for settings.

## Health checks

`api/health.rs` follows ASP.NET Core's health checks, so operators and
monitoring that know one know the other.

- A check implements `HealthCheck` and returns a `HealthCheckResult`
  (`healthy`, `degraded` or `unhealthy`, a description, optional `data`), or
  `Err(description)` when it fails.
- Checks are registered by name where the app state is built (`api.rs`):
  `HealthChecks::new().add("database", DatabaseCheck(store.clone()))`.
  `add_with_failure_status(name, HealthStatus::Degraded, check)` is for a
  dependency the service can work without: its failure degrades the service
  instead of taking it out of rotation.
- A probe's status is the worst of its checks. A check that takes longer than
  the timeout, or panics, has failed. Checks run at the same time.
- The default answer is one word of plain text, `Healthy`, `Degraded` or
  `Unhealthy`, with 200, 200 and 503, and headers that forbid caching.
  `?format=json` gives the detail in the shape of the ASP.NET Core docs:

```json
{
  "status": "Healthy",
  "results": {
    "database": { "status": "Healthy", "description": "The database answered.", "data": {} }
  }
}
```

`/health/live` runs no checks: if it touched a dependency, a database outage
would make the orchestrator restart healthy processes. `/health/ready` runs
every registered check. When the service gains another dependency (a cache,
another API), register a check for it; do not add an endpoint. Descriptions
are shown to whoever can call the probe, so they never contain connection
strings, host names or raw error messages: log the cause, describe the effect.
A container `HEALTHCHECK` should call `/health/ready`.

## Verifying

Do all of these before reporting the work as done:

1. `cargo fmt --check`, `cargo clippy --all-targets` (no warnings), `cargo test`.
2. Run the binary and look at the real thing: `cargo run`, then request the
   new endpoints, one failing request (check the `content-type` is
   `application/problem+json`), `/health/ready?format=json` and
   `/api/openapi.json`.
3. PostgreSQL is separate code that the tests do not reach. When the store
   changed, run against a real one and repeat the requests:

   ```sh
   # pick another name or port if these are taken
   docker run --rm -d --name api-pg -e POSTGRES_PASSWORD=pg -p 55432:5432 postgres:17-alpine
   cargo run -- --database-url postgres://postgres:pg@127.0.0.1:55432/postgres
   docker rm -f api-pg
   ```

   If Docker is not available, say that the PostgreSQL path was not exercised.

## Related work this skill does not cover

Container images, release workflows and compose files have their own
conventions; follow the user's instructions or another skill for those. For a
browser client served from a different origin, read `references/cors.md`.
