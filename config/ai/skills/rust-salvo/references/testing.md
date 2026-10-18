# Testing

`salvo::test` (default feature `test`) runs requests against a `Service`, `Router` or `Handler` in memory, with no
socket. The template's `tests/api.rs` covers the happy path, every problem response and the OpenAPI document.

```rust
use salvo::prelude::*;
use salvo::test::{ResponseExt, TestClient};

fn app() -> Service {
    todo_salvo::service(Store::default())       // a fresh app, so tests run in parallel
}

#[tokio::test]
async fn creates_a_todo() {
    let mut res = TestClient::post("http://127.0.0.1:8080/api/v1/todos")
        .json(&serde_json::json!({ "title": "write tests" }))
        .send(&app())
        .await;

    assert_eq!(res.status_code, Some(StatusCode::CREATED));
    let todo: Todo = res.take_json().await.unwrap();
    assert_eq!(todo.title, "write tests");
}
```

- Builders: `TestClient::get|post|put|patch|delete|head|options|trace(url)`, then `.query(k, v)`, `.queries(pairs)`,
  `.add_header(name, value, overwrite)`, `.basic_auth(user, Some(pass))`, `.bearer_auth(token)`, `.json(&v)`,
  `.raw_json(str)`, `.form(&v)`, `.raw_form(str)`, `.text(str)`, `.bytes(vec)`, `.body(..)`, and finally
  `.send(target)`.
- `send` takes `&Service`, a `Router` or a handler. Test the `Service` your `main` uses, so the catcher and
  service hoops are part of the test; a bare router answers errors with the default HTML catcher.
- The scheme, host and port of the URL are ignored; only path and query are routed.
- `ResponseExt`: `take_string()`, `take_json::<T>()`, `take_bytes(None)`. They consume the body; a second call returns
  empty. Read `res.status_code` and `res.headers()` before taking the body if you need both in one expression
  (borrow rules).
- There is no peer address: `req.remote_addr()` is unknown, so `RemoteIpIssuer` rate limiting answers 400. Use a
  closure issuer in tests, or test the limiter over real HTTP.
- HEAD responses have no body and no content type.

Real HTTP (for TLS, WebSocket, the rate limiter or shutdown): bind `TcpListener::new("127.0.0.1:0")`, get the port
with `acceptor.holdings()[0].local_addr.clone().into_std()` (`use salvo::conn::Acceptor;`; it is Salvo's own
`SocketAddr`, which also covers Unix sockets), spawn `Server::new(acceptor).serve(..)` and call it with `reqwest`
or `curl`.