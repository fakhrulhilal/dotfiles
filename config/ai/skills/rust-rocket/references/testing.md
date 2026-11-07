# Testing

## The local client

```rust
use rocket::local::asynchronous::Client;

#[rocket::async_test]
async fn creates_a_todo() {
    let client = Client::untracked(app()).await.unwrap();   // ignites: fairings run, sentinels checked
    let response = client
        .post("/api/v1/todos")
        .json(&serde_json::json!({ "title": "Buy milk" }))   // sets Content-Type: application/json
        .dispatch()
        .await;
    assert_eq!(response.status(), Status::Created);
    assert_eq!(response.headers().get_one("location").is_some(), true);
    let todo: Todo = response.into_json().await.unwrap();      // feature `json`
}
```

- `Client::tracked` keeps cookies between requests, `untracked` does not. `rocket::local::blocking::Client` is the
  same without `.await`, for sync tests (it cannot run requests concurrently).
- A dispatch goes through everything a network request does: routing, guards, catchers, fairings, Shield, HEAD
  body stripping *(run)*. There is no socket, so it is fast and needs no free port.
- Requests: `.header(ContentType::JSON)`, `.header(Header::new("origin", ".."))`, `.body(..)`, `.json(..)`,
  `.cookie(..)`. Responses: `.status()`, `.content_type()`, `.headers().get_one(name)`, `.into_string()`,
  `.into_bytes()`, `.into_json::<T>()`.
- Build the app with an in-memory database or store per test; `Client::untracked` runs ignite fairings, so a fairing
  that opens the database opens a fresh one each time.

## Unit tests of a responder or guard

A responder needs a request: dispatch through a small Rocket mounted with only that route, or keep the responder a thin
wrapper over a plain value and test that value. Usually the local client is simpler: todo-app's `backend/rocket`
tests its health probes only through it.

## A real server, when it matters

TLS, HTTP/2 or keep-alive behaviour need a socket: set `port: 0`, launch in a task, and read the bound port in an
`on_liftoff` fairing (`rocket.config().port` is updated after binding), sending it out through a `oneshot` channel.
Stop it with `rocket.shutdown().notify()` from the same fairing's handle.
