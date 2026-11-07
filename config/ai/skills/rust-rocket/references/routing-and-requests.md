# Routing and reading requests

Rocket 0.5.2. Claims marked *(run)* were run against it.

## Routes and mounting

```rust
#[rocket::get("/todos/<id>")]                       // method + path; `<id>` binds the handler argument `id`
async fn get(id: Uuid) -> String { id.to_string() }

#[rocket::post("/todos", data = "<input>")]         // the body: exactly one data guard
async fn create(input: Json<CreateTodo>) -> Status { Status::Created }

#[rocket::get("/files/<path..>")]                   // trailing segments (FromSegments), PathBuf is traversal-safe
async fn file(path: std::path::PathBuf) {}

rocket::build()
    .mount("/api/v1", rocket::routes![get, create]) // route paths are relative to the mount base
```

- Method attributes: `get`, `put`, `post`, `delete`, `head`, `patch`, `options`, or `#[route(GET, uri = "..")]`.
- `<_>` ignores one segment, `<_..>` the rest. A `Route::new(Method, "/path/<_>", handler)` built by hand takes the
  same syntax (used for the 405 routes in `assets/template/src/problem.rs`).
- Two routes with the same method, path and rank **collide**, and launch fails (`ErrorKind::Collisions`). Routes that
  differ in method never collide.
- Ranks: lower runs first. Defaults run from -12 to -1 (static paths and queries before dynamic ones). Give a
  catch-all a high rank (`Route::ranked(100, ..)`).

## Path matching *(run)*

- Rocket matches the **non-empty segments** of a path: `/api/v1/todos/`, `//api/v1/todos`, `/api//v1/todos` and
  `/api/v1/todos//` all reach the `/api/v1/todos` route. axum and actix answer those 404. There is no switch; to
  refuse them, check `request.uri().path()` yourself.
- `request.uri().path()` is the path as sent (with the doubled slashes), without the query string.

## No 405 *(run)*

A known path with another method is a **404**: a route only matches its own method. To answer 405 with `Allow`, add
one route per other method on that path, after the real ones (`method_not_allowed` in the template). Leave HEAD out
when GET is allowed: Rocket answers HEAD with the GET route (body dropped) only when no HEAD route matches, so a HEAD
fallback would win.

## Path parameters

- Any `FromParam` type: integers, `bool`, `&str`, `String`, `Uuid` (feature `uuid`), your own.
- A value that does not parse **forwards with 422** (the codegen's `Outcome::Forward((data, Status::UnprocessableEntity))`):
  the next matching route is tried, and when none is left the 422 catcher runs. For a 400 with a message, take
  `Result<T, T::Error>` and map the error in the handler:

```rust
#[rocket::get("/todos/<id>")]
async fn get(id: Result<Uuid, rocket::serde::uuid::Error>) -> Result<Json<Todo>, ApiError> {
    let id = id.map_err(|e| ApiError::Rejected(Status::BadRequest, format!("Invalid URL: {e}")))?;
    // ...
}
```

`Option<T>` swallows the error (`None`).

## Query strings

```rust
#[rocket::get("/todos?<status>&<page>")]            // each a FromFormField / FromForm
async fn list(status: Option<String>, page: Option<u32>) {}

#[rocket::get("/search?<query..>")]                  // a whole FromForm struct
async fn search(query: Search<'_>) {}

#[rocket::get("/v?lang=en")]                         // static: must be present to match
async fn english() {}
```

- Lenient by default: unknown and duplicate parameters are ignored, a missing `Option` is `None`.
- A value that fails to parse forwards (422), like a path value. `Option<T>` makes it `None` silently: a typo like
  `?status=actve` would fall back to the default. To answer 400, take the text and parse it in the handler. Reading
  it with serde gives serde's messages ("unknown variant `x`, expected one of `all`, `active`, `completed`"):

```rust
use serde::de::{Deserialize, value::{Error, StrDeserializer}};
let filter = StatusFilter::deserialize(StrDeserializer::<Error>::new(&text))?;
```

- An enum used directly as a query value needs `#[derive(FromFormField)]`; a type from another crate cannot get it,
  so the serde route is also how to use shared contract types.

## Body (data guards)

- `rocket::serde::json::Json<T>` (feature `json`), `Form<T>`, `String`, `Vec<u8>`, `Data<'_>` (stream, `open(limit)`
  required), `TempFile`, `Capped<T>`.
- Rocket's `Json<T>` **does not look at `Content-Type`** *(read in source)*: `text/plain` with a JSON body is
  accepted. Its errors: `Status::PayloadTooLarge` (413) over the `json` limit, `UnprocessableEntity` (422) for JSON
  of the wrong shape (serde's `Category::Data`), `BadRequest` (400) for anything else (syntax, EOF, I/O).
- `format = "json"` on a POST/PUT/PATCH/DELETE route matches `Content-Type`; a mismatch **forwards** (another route,
  or a 404), not 415. To answer 415 write a data guard that checks `request.content_type()` first and then calls
  `rocket::serde::json::Json::<T>::from_data(request, data)`, as the template's `Json<T>` does.
- Limits come from the config: `Limits::default().limit("json", 2.mebibytes())`. Defaults: json 1 MiB, form 32 KiB,
  data-form 2 MiB, file 1 MiB, string and bytes 8 KiB.
- `Result<Json<T>, json::Error<'_>>` as the guard type gives the handler the error but **drops the status** Rocket
  picked; recompute it from the error if you go that way.

## Request guards

`FromRequest` types run before the data guard, left to right, and the first failure stops the rest: `&State<T>`,
`&Host`, `&ContentType`, `&CookieJar`, `Shutdown`, `IpAddr`, your own. `Option<G>` and `Result<G, G::Error>` catch a
failure. A guard fails with `Outcome::Error((status, error))` (catcher for `status`) or forwards
(`Outcome::Forward(status)`).

## HEAD and OPTIONS

- GET routes answer HEAD; Rocket strips the body *(run)*.
- OPTIONS is just a method: no automatic answer. CORS preflights need a route or a response fairing
  (fairings-and-state.md).
