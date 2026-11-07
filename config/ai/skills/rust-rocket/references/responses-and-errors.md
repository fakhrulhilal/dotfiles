# Responses, errors and problem details

## Responders

A handler returns anything that implements `Responder<'r, 'o>`:

| Type                                   | Response                                                  |
|----------------------------------------|-----------------------------------------------------------|
| `&str`, `String`                       | 200 `text/plain`                                          |
| `Json<T>` (`rocket::serde::json`)      | 200 `application/json`                                    |
| `(ContentType, T)`                     | `T` with that content type, e.g. `(ContentType::JSON, already_serialized)` |
| `(Status, T)`, `status::Custom(s, T)`  | `T` with that status                                      |
| `Status`                               | 100 and 200 to 205: empty response. 4xx/5xx: **runs that status's catcher**. Anything else (3xx, 206+): logged and turned into a 500 |
| `status::Created::new(location).body(T)` | 201 with `Location`                                    |
| `status::NoContent`                    | 204                                                       |
| `Redirect::temporary(uri)` / `to` / `permanent` | 307 / 303 / 308 with `Location`                  |
| `Option<T>`                            | `None` is a 404 (the catcher)                             |
| `Result<T, E>`                         | either one's response, both responders                    |

Write your own when headers or the status depend on data:

```rust
impl<'r> Responder<'r, 'static> for Problem {
    fn respond_to(self, request: &'r Request<'_>) -> response::Result<'static> {
        let body = serde_json::to_vec(&Problem { instance: Some(request.uri().path().to_string()), ..self })
            .unwrap_or_default();
        Response::build()
            .status(Status::new(self.status))
            .header(ContentType::new("application", "problem+json"))
            .sized_body(body.len(), std::io::Cursor::new(body))
            .ok()
    }
}
```

`respond_to` sees the request, so request details (`instance`) belong there and not in middleware.
`#[derive(Responder)]` works for wrapper structs and enums whose fields are responders and headers.

## Catchers

```rust
#[rocket::catch(404)]
fn not_found(request: &Request<'_>) -> Problem { .. }

#[rocket::catch(default)]                              // every status without its own catcher
fn other(status: Status, request: &Request<'_>) -> Problem { .. }

rocket.register("/", rocket::catchers![not_found, other])   // scoped by base; the longest base wins
```

- A catcher runs when: no route matched (404), every route forwarded (the last forward's status), a guard or handler
  failed with `Outcome::Error((status, _))`, a handler returned an error `Status`, a handler **panicked** (500), or
  Rocket could not read the request (400).
- Arguments: none, `&Request`, or `(Status, &Request)`. No guards, and **not the guard's error**. Cookie changes are
  dropped before it runs.
- Without registered catchers, Rocket answers HTML, or JSON when `Accept` asks for it: not a problem document.

### Getting a guard's error into the catcher

Leave it in the request-local cache, keyed by a type of your own, then fail with the status:

```rust
struct Rejection(Option<Problem>);

// in the guard:
request.local_cache(|| Rejection(Some(problem)));          // the first value set wins
return data::Outcome::Error((status, ()));

// in the default catcher:
match &request.local_cache(|| Rejection(None)).0 {
    Some(problem) if problem.status == status.code => problem.clone(),
    _ => Problem::new(status),
}
```

## RFC 9457 problem details, end to end

The template's `src/problem.rs` covers every way a request can end in an error:

| Error                                   | Where it becomes a problem                                       |
|-----------------------------------------|------------------------------------------------------------------|
| Handler returns `Err(ApiError)`         | `ApiError: Responder`                                            |
| Body: not JSON (415), malformed (400), wrong shape (422), too large (413) | `Json<T>` data guard → local cache → default catcher |
| Path value does not parse               | handler takes `Result<T, E>`, maps to 400                        |
| Query value does not parse              | handler takes `Option<String>`, parses with serde, 400           |
| Unknown path                            | `#[catch(404)]`                                                  |
| Known path, other method                | 405 routes with `Allow` (Rocket has no 405)                      |
| Panic, unreadable request               | `#[catch(default)]` with the status alone                        |

`Status::reason()` gives the title (`"Unprocessable Entity"`, `"Payload Too Large"`): the same words as the `http`
crate's `canonical_reason()`.

## Logging handler failures

Keep internal causes out of the body: log them where `ApiError` turns into a problem (`tracing::error!`), and answer
a generic 500 detail.
