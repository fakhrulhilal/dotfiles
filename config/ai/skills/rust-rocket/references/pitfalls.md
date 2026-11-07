# Pitfalls

Each entry was met or reproduced with Rocket 0.5.2 (tokio, macOS, Rust 1.99, edition 2024), most of them while
porting todo-app's axum backend to Rocket.

## Compile errors

| Message (abridged)                                                              | Cause and fix                                                                                       |
|---------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------|
| `no method named ends_with found for reference &UncasedStr`                     | `ContentType::sub()`/`top()` return `&UncasedStr`. Compare with `==` (case-insensitive), or `.as_str()` first. |
| `unresolved import rocket::serde::json`                                         | Feature `json` is off.                                                                              |
| `Uuid: FromParam is not satisfied`                                              | Feature `uuid` is off. It re-exports uuid 1 as `rocket::serde::uuid::Uuid`, the same type as your `uuid` 1 dependency. |
| `missing field __non_exhaustive` building `rocket::Config` / `Shutdown`         | Both are non-exhaustive: end the literal with `..rocket::Config::default()` / `..Shutdown::default()`. |
| `utoipa::path` and `#[rocket::get]` on one function                             | They compose: put `#[utoipa::path(..)]` first (outermost). Both generate items named after the function (`__path_list` and `list`), which do not clash. |

## Runtime surprises

- **No 405.** A known path with another method is a 404. Add 405 routes per path (routing-and-requests.md).
- **Trailing and doubled slashes match.** `/todos/` and `//todos` reach the `/todos` route. There is no strict mode.
- **A bad path or query value forwards with 422**, then the 422 catcher runs, without the parse error. Take
  `Result<T, E>` (path) or the raw text (query) and answer 400 yourself.
- **`Option<T>` on a query value hides typos**: `?status=actve` is `None`, the default.
- **Rocket's `Json` ignores `Content-Type`.** `text/plain` with a JSON body is accepted; `format = "json"` forwards
  (404) instead of 415. Check the content type in your own data guard.
- **The default JSON limit is 1 MiB**, not axum's 2 MiB: set `limits.json`.
- **Catchers never see a guard's error.** Leave it in `request.local_cache` and read it in the catcher.
- **`local_cache` keeps the first value** set for a type. Use a distinct type per use (`Started`, `Rejection`).
- **A 3xx `Status` as a responder is a 500**: only 100, 200 to 205 and errors are accepted. Use `Redirect` or
  `(Status, T)`.
- **Rocket's log lines show up through tracing** even with `log_level: Off`, because tracing-subscriber installs the
  `log` bridge. Filter the `rocket` target.
- **Top-level `env` or `log` keys in the figment** make Rocket warn about deprecated keys. Nest app settings under a
  key of their own.
- **`address` must be an IP**: `localhost` fails config extraction. Resolve or validate before building the config.
- **A dropped `rocket::Error` panics.** Read it (`kind()`, `Display`) before dropping; its `Display` is terse, so
  match `kind()` for a useful message.
- **A failed ignite fairing only reports its name**: log the cause inside the fairing.
- **Shield adds three security headers** to every response. Harmless for an API; replace with `Shield::new()` if
  they are not wanted.
- **`#[rocket::main]` reads `workers` from `ROCKET_WORKERS`/`Rocket.toml`**, even when `rocket::custom` is given a
  figment without them.
- **A HEAD route on a GET path takes precedence** over Rocket's automatic HEAD; keep HEAD out of fallback routes.
- **`&State<T>` for an unmanaged `T` aborts the launch** (sentinel) rather than failing per request: a good thing,
  but it means an ignite fairing that fails silently stops the app from starting.
