//! The whole app in memory: `test::init_service` builds it like a worker would, `TestRequest` makes requests.

use actix_web::{
    body::MessageBody,
    dev::{Service, ServiceResponse},
    http::{Method, StatusCode, header},
    test::{self, TestRequest},
    web::Data,
};
use serde_json::{Value, json};
use todo_actix::{
    app,
    problem::{PROBLEM_JSON, Problem},
    todos::{Todo, TodoStore},
};

const ORIGIN: &str = "https://web.example";

async fn service()
-> impl Service<actix_http::Request, Response = ServiceResponse<impl MessageBody>, Error = actix_web::Error> {
    test::init_service(app(Data::new(TodoStore::default()), vec![ORIGIN.to_owned()])).await
}

/// actix's `HeaderMap` has no `Index` impl: `headers()[NAME]` does not compile.
fn header<B>(response: &ServiceResponse<B>, name: header::HeaderName) -> &str {
    response
        .headers()
        .get(name)
        .and_then(|value| value.to_str().ok())
        .unwrap_or_default()
}

async fn problem(response: ServiceResponse<impl MessageBody>) -> Problem {
    assert_eq!(header(&response, header::CONTENT_TYPE), PROBLEM_JSON);
    test::read_body_json(response).await
}

#[actix_web::test]
async fn todo_lifecycle() {
    let app = service().await;

    let request = TestRequest::post()
        .uri("/api/v1/todos")
        .set_json(json!({ "title": " Buy milk " }));
    let response = test::call_service(&app, request.to_request()).await;
    assert_eq!(response.status(), StatusCode::CREATED);
    assert_eq!(header(&response, header::LOCATION), "/api/v1/todos/1");
    let milk: Todo = test::read_body_json(response).await;
    assert_eq!(milk.title, "Buy milk");

    let request = TestRequest::patch()
        .uri("/api/v1/todos/1")
        .set_json(json!({ "completed": true }));
    let done: Todo = test::call_and_read_body_json(&app, request.to_request()).await;
    assert!(done.completed);

    let request = TestRequest::get().uri("/api/v1/todos?completed=true");
    let list: Vec<Todo> = test::call_and_read_body_json(&app, request.to_request()).await;
    assert_eq!(list, [done]);

    let request = TestRequest::delete().uri("/api/v1/todos/1");
    assert_eq!(
        test::call_service(&app, request.to_request()).await.status(),
        StatusCode::NO_CONTENT
    );
    let request = TestRequest::delete().uri("/api/v1/todos/1");
    let body = problem(test::call_service(&app, request.to_request()).await).await;
    assert_eq!(body.status, 404);
    assert_eq!(body.instance.as_deref(), Some("/api/v1/todos/1"));
}

#[actix_web::test]
async fn unreadable_requests_are_problems() {
    let app = service().await;
    let post = |body: &'static str, content_type: &'static str| {
        TestRequest::post()
            .uri("/api/v1/todos")
            .insert_header((header::CONTENT_TYPE, content_type))
            .set_payload(body)
            .to_request()
    };

    for (request, status) in [
        (post("{bad", "application/json"), 400),
        (post(r#"{"name":"x"}"#, "application/json"), 422),
        (post(r#"{"title":"x"}"#, "text/plain"), 415),
        (post(r#"{"title":"   "}"#, "application/json"), 422),
        (TestRequest::get().uri("/api/v1/todos/abc").to_request(), 400),
        (
            TestRequest::get().uri("/api/v1/todos?completed=maybe").to_request(),
            400,
        ),
    ] {
        let body = problem(test::call_service(&app, request).await).await;
        assert_eq!(body.status, status, "{body:?}");
        assert!(body.instance.unwrap().starts_with("/api/v1/todos"));
    }

    let request = post(r#"{"title":"  "}"#, "application/json");
    let body: Value = test::call_and_read_body_json(&app, request).await;
    assert_eq!(body["errors"]["title"], json!(["must not be empty"]));
}

#[actix_web::test]
async fn router_errors_are_problems() {
    let app = service().await;

    // The resource's built-in 405 lists the methods its routes' guards checked.
    let response = test::call_service(&app, TestRequest::put().uri("/api/v1/todos").to_request()).await;
    let allow = header(&response, header::ALLOW).to_owned();
    for method in ["GET", "HEAD", "POST"] {
        assert!(allow.contains(method), "{allow}");
    }
    assert_eq!(problem(response).await.status, 405);

    // No trailing-slash or empty-segment normalisation unless `NormalizePath` is added.
    for path in ["/nothing", "/api/v1/todos/", "//api/v1/todos", "/api/v1"] {
        let response = test::call_service(&app, TestRequest::get().uri(path).to_request()).await;
        let body = problem(response).await;
        assert_eq!(body.status, 404, "{path}");
        assert_eq!(body.instance.as_deref(), Some(path));
    }
}

#[actix_web::test]
async fn head_is_answered_by_get_routes() {
    let app = service().await;
    let request = TestRequest::default()
        .method(Method::HEAD)
        .uri("/api/v1/todos")
        .to_request();
    let response = test::call_service(&app, request).await;
    assert_eq!(response.status(), StatusCode::OK);
}

#[actix_web::test]
async fn cors() {
    let app = service().await;

    let preflight = TestRequest::default()
        .method(Method::OPTIONS)
        .uri("/api/v1/todos")
        .insert_header((header::ORIGIN, ORIGIN))
        .insert_header((header::ACCESS_CONTROL_REQUEST_METHOD, "POST"))
        .to_request();
    let response = test::call_service(&app, preflight).await;
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(header(&response, header::ACCESS_CONTROL_ALLOW_ORIGIN), ORIGIN);

    // Errors carry the header too, or the browser could not read the problem.
    let request = TestRequest::get()
        .uri("/nothing")
        .insert_header((header::ORIGIN, ORIGIN))
        .to_request();
    let response = test::call_service(&app, request).await;
    assert_eq!(header(&response, header::ACCESS_CONTROL_ALLOW_ORIGIN), ORIGIN);

    // Another site: served, without the header.
    let request = TestRequest::get()
        .uri("/health")
        .insert_header((header::ORIGIN, "https://other.example"))
        .to_request();
    let response = test::call_service(&app, request).await;
    assert_eq!(response.status(), StatusCode::OK);
    assert!(!response.headers().contains_key(header::ACCESS_CONTROL_ALLOW_ORIGIN));

    // A preflight it refuses: 400, as a problem.
    let preflight = TestRequest::default()
        .method(Method::OPTIONS)
        .uri("/api/v1/todos")
        .insert_header((header::ORIGIN, "https://other.example"))
        .insert_header((header::ACCESS_CONTROL_REQUEST_METHOD, "POST"))
        .to_request();
    let body = problem(test::call_service(&app, preflight).await).await;
    assert_eq!(body.status, 400);
}
