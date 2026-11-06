# Testing

From the actix.rs Testing page and the `actix_web::test` docs. The template's `tests/api.rs` uses the in-memory
style; a real-server test is below. Both were run against actix-web 4.18.1.

## The whole app in memory

```rust
use actix_web::{test::{self, TestRequest}, http::{StatusCode, header}};

#[actix_web::test]
async fn creates_a_todo() {
    let app = test::init_service(app(Data::new(Store::default()), settings())).await;

    let request = TestRequest::post().uri("/api/v1/todos").set_json(json!({ "title": "milk" })).to_request();
    let response = test::call_service(&app, request).await;
    assert_eq!(response.status(), StatusCode::CREATED);
    let todo: Todo = test::read_body_json(response).await;

    let list: Vec<Todo> = test::call_and_read_body_json(&app, TestRequest::get().uri("/api/v1/todos").to_request()).await;
    assert_eq!(list, [todo]);
}
```

| Helper                                        | Does                                                                |
|-----------------------------------------------|---------------------------------------------------------------------|
| `test::init_service(App)`                     | builds the app like a worker would (middleware, app data, factories) |
| `test::call_service(&app, req)`               | `ServiceResponse`; panics on a middleware `Err` (`try_call_service` returns it) |
| `test::call_and_read_body(&app, req)`         | body as `Bytes`                                                     |
| `test::call_and_read_body_json(&app, req)`    | body deserialized; panics on a bad body (`try_..` variant returns `Result`) |
| `test::read_body(res)`, `read_body_json(res)` | read a `ServiceResponse` you already have                           |
| `TestRequest::get()/post()/put()/patch()/delete()/default().method(m)` | request builder: `uri`, `insert_header`, `set_json`, `set_form`, `set_payload`, `param`, `peer_addr`, `app_data`, `cookie` |
| `.to_request()`                               | for `call_service`                                                  |
| `.to_http_request()` / `.to_srv_request()` / `.to_http_parts()` | for calling handlers, extractors and middleware directly |
| `test::ok_service()`, `status_service(code)`, `simple_service(code)` | trivial inner services for middleware unit tests |

To name the type of the initialised app in a helper:

```rust
async fn service() -> impl actix_web::dev::Service<
    actix_http::Request, Response = ServiceResponse<impl MessageBody>, Error = actix_web::Error,
> {
    test::init_service(app(Data::new(Store::default()))).await
}
```

(`actix-http` as a dev-dependency for `actix_http::Request`.)

actix's `HeaderMap` has no `Index` impl: `response.headers()[header::LOCATION]` does not compile. Use
`response.headers().get(header::LOCATION).unwrap()`, or a small helper returning `&str`.

## A real server on a random port

For tests that need real HTTP (connection behaviour, HEAD bodies, clients such as reqwest):

```rust
async fn spawn_app() -> String {
    let state = Data::new(AppState::new(store().await));
    let server = HttpServer::new(move || app(state.clone()))
        .workers(1)
        .disable_signals()
        .bind(("127.0.0.1", 0))
        .unwrap();
    let base_url = format!("http://{}", server.addrs()[0]);
    actix_web::rt::spawn(server.run()); // stops with the test's runtime
    base_url
}

#[actix_web::test]
async fn health() {
    let base_url = spawn_app().await;
    let body = reqwest::get(format!("{base_url}/health")).await.unwrap().text().await.unwrap();
    assert_eq!(body, "Healthy");
}
```

The separate `actix-test` crate offers `actix_test::start(|| app())` with an awc client, if adding awc is fine.

## Handlers, extractors and middleware alone

- A handler without a routing macro is a plain function: call it with extractor values built by hand, or with
  `TestRequest::default().to_http_request()` for an `HttpRequest` argument, then assert on its `HttpResponse`.
- An extractor: `T::extract(&TestRequest::default().insert_header(..).to_http_request()).await`.
- A responder: `value.respond_to(&req)`; read the body with `actix_web::body::to_bytes(res.into_body()).await`.
- A middleware: `let mw = MyMw.new_transform(test::ok_service()).await.unwrap();` then
  `test::call_service(&mw, TestRequest::default().to_srv_request()).await`.

## Runtime

`#[actix_web::test]` starts an actix `System` (current-thread tokio with a `LocalSet`) per test. Plain
`#[tokio::test]` works for code that does not need `actix_web::rt::spawn` or a `System`; it is the way to use
`start_paused = true` for time-dependent tests.
