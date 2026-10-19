# Pitfalls already hit once

Read the section for the part you are changing.

## axum

- Path parameters are written `{id}` (axum 0.8), not `:id`. The old form
  compiles and never matches.
- axum answers some failures itself with an empty or plain-text body: unknown
  path (404), wrong method (405), unreadable JSON (400/415/422), bad path or
  query value (400). Three things together cover them, and all three are
  needed:
  - the extractors in `api/extract.rs` (rejections become `ApiError`);
  - `.fallback(unknown_endpoint)` for unknown paths;
  - `.layer(middleware::from_fn(problem::problem_details))`, which gives any
    remaining error without a content type a problem body and keeps the
    response's other headers, such as the `Allow` of a 405.
- `instance` needs the request, which `IntoResponse` does not have. So
  `Problem::into_response` leaves a copy of itself in the response extensions
  and `problem_details` rebuilds the response with `instance` set. It uses the
  path only: a query string can carry tokens and other values that should not
  be echoed back or logged by clients.
- The layer is added on the outer router, after `.nest(...)`, so
  `request.uri()` is still the full path (`/api/v1/todos/...`). Inside a nested
  router the prefix is stripped.
- Merge `SwaggerUi` *after* that layer, as `api.rs` does, so the layer does not
  touch the Swagger UI's own responses.
- The request-log middleware wraps everything, so it is added in `lib.rs`
  around the router, not inside `api.rs`.

## utoipa

- `OpenApiRouter::routes(routes!(a, b))`: handlers in one `routes!` call must
  share the same path. Use one call per path (`/todos`, `/todos/{id}`).
- Paths in `#[utoipa::path]` are relative to where the router is nested.
  `.nest("/api/v1", todos::router())` makes the documented path
  `/api/v1/todos`. Health routes are merged without nesting, so they carry the
  full path.
- `split_for_parts()` returns the axum router and the finished document. Apply
  `.with_state(...)` before splitting.
- Query structs derive `IntoParams` with `#[into_params(parameter_in = Query)]`
  and are listed as `params(ListTodos)`. Path parameters are listed inline:
  `params(("id" = Uuid, Path, description = "..."))`.
- `utoipa-swagger-ui` needs the `vendored` feature. Without it the build
  downloads the Swagger UI archive, which fails in offline and sandboxed
  builds (container builds included).
- The `Problem` struct renames `type_uri` to `type` with serde; utoipa follows
  the serde rename, so the schema is correct without extra attributes.

## sqlx (PostgreSQL)

- `default-features = false` with `runtime-tokio`, `tls-rustls-ring-webpki`,
  `postgres`, `migrate`, `macros`, `uuid`, `chrono`. The default features pull
  in every database driver. `ring` compiles a little C, which matters for
  static and cross builds.
- Queries use `sqlx::query` / `query_as` (checked at run time), not the
  `query!` macros, so building needs no database and no `.sqlx` directory.
- `sqlx::migrate!("migrations/postgres")` embeds the directory at compile
  time, relative to the crate root. After adding a migration file, a build
  may not notice; touch `src/store/postgres.rs` or run `cargo clean -p <crate>`
  if the new migration is not applied.
- An optional filter in one query: `WHERE $1::boolean IS NULL OR completed = $1`.
  The cast is required; PostgreSQL cannot infer the type of a bare `$1 IS NULL`.
- Partial update in one statement: `SET title = COALESCE($2, title)` with
  `RETURNING ...`; `fetch_optional` gives `None` for an unknown id.

## Turso (embedded, SQLite-compatible)

- `turso = { default-features = false }`. It is not rusqlite and sqlx cannot
  drive it, hence the separate implementation and the small migration runner
  in `store/turso.rs` (`_migrations` table, files listed in `MIGRATIONS`).
- There is no pool. `Database::connect()` is cheap; take a connection per
  operation and set `busy_timeout` on it, or concurrent writers fail at once
  with "database is locked".
- Parameters: a homogeneous array (`[id.to_string()]`) or a `Vec<Value>` for
  mixed types (`Value::Text`, `Value::Integer`, `Value::Null`). `()` for none.
- No boolean, UUID or timestamp column types: store 0/1, text and fixed-width
  RFC 3339 text. Fixed width (`SecondsFormat::Micros`, `Z` suffix) is what
  makes `ORDER BY created_at` chronological.
- `UPDATE ... RETURNING` is avoided: update, check the changed-row count, then
  read the row back.
- `rows` borrows the connection; `drop(rows)` before running the next
  statement on the same connection (see `migrate`).
- `sqlite::memory:` gives a database that lives as long as the `Database`
  value. Tests use it; each `spawn_app()` gets its own.
- `connect()` in `store.rs` accepts `sqlite://relative/path`,
  `sqlite:///absolute/path`, `sqlite:path` and `sqlite::memory:`, and creates
  the parent directory. In YAML, quote `"sqlite::memory:"`.

## Configuration and logging

- figment with `features = ["yaml", "env"]` (and `"test"` as a
  dev-dependency for `Jail`). Sources merge in registration order, the later
  one winning: `Yaml::string(DEFAULTS)`, the file, `Env`, then the arguments
  through `Serialized::defaults(&cli.settings)`.
- `Yaml::file` searches parent directories and ignores a missing file;
  `Yaml::file_exact` does neither and fails when the file is missing. So the
  default `config.yaml` is merged only if it exists, and the one given with
  `--config` always, which makes a wrong path an error.
- `Env::raw()` would turn every environment variable into a setting. The
  filter keeps only the names derived from the keys of the defaults
  (`server.port` is `SERVER__PORT`), then `.split("__")` nests them.
- The `...Args` structs mirror the shape of `Config` and serialize only the
  arguments that were given (`skip_serializing_if = "Option::is_none"`).
  Without that, an absent argument would merge as null over the real value.
  Nested fields need an explicit `long = "server-port"`; clap would otherwise
  name the argument after the field alone (`--port`).
- Do not give a clap argument a default: it would always win over the file
  and the environment. Defaults live in `config/default.yaml` only.
- Environment variables are not shown in `--help` any more (figment reads
  them, not clap), so the convention is stated once in `after_help`.
- `figment::Error` is large; `Config::load` returns it boxed to keep clippy's
  `result_large_err` quiet. Its message names the key and the source, e.g.
  `expected u16 for key "SERVER.PORT" in environment variable(s)`.
- Tests of precedence use `figment::Jail`: a temporary directory and a
  private environment, so they neither see nor change the real ones.
- With no prefix, `ENV` and `CONFIG` are short, common names. If the service
  must share an environment with something that uses them differently, that
  is the reason to rename the key, in all three places.
- Error messages and logs must not contain the database URL: it carries the
  password. `store::redact` keeps only the scheme.
- `main` prints `error: {error:#}` (the whole cause chain on one line) and
  exits non-zero; logging may not be initialised yet when configuration fails.

## Tests

- A file directly under `tests/` is its own crate and its own binary. Shared
  helpers and a module per source file therefore live in a directory:
  `tests/api/main.rs` plus `mod` files. A new test file that is not declared
  with `mod` in `main.rs` is silently never compiled.
- `spawn_app()` binds `127.0.0.1:0` and serves the real `app(...)`, so tests
  cover routing, extractors, middleware and serialisation together. Do not
  call handlers directly.
- reqwest with `default-features = false, features = ["json"]`: the tests
  only speak plain HTTP to localhost and need no TLS stack.
- Assert the content type of error responses, not only the status: a 404 with
  an empty body has the right status and the wrong contract.
