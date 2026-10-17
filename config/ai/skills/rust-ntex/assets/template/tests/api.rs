//! In-memory tests call the application service directly; `over_http` runs a real server and client.
use ntex::Pipeline;
use ntex::http::header::{CONTENT_TYPE, LOCATION};
use ntex::http::{Method, Request, Response, StatusCode};
use ntex::web::{WebError, test};
use serde_json::{Value, json};
use todo_ntex::{ApiError, AppState, Store};

type App = Pipeline<Request, Response, WebError<AppState, ApiError>>;

async fn app() -> App {
    test::init_service_st(AppState::new(Store::default()), todo_ntex::app()).await
}

async fn send(app: &App, method: Method, uri: &str, body: Option<Value>) -> Response {
    let req = test::TestRequest::default().method(method).uri(uri);
    let req = match body {
        Some(body) => req.json(&body),
        None => req,
    };
    test::call_service(app, req.to_request()).await
}

async fn json_body(res: Response) -> Value {
    serde_json::from_slice(&test::read_body(res).await).unwrap()
}

fn assert_problem(res: &Response, status: StatusCode) {
    assert_eq!(res.status(), status);
    assert_eq!(res.headers().get(CONTENT_TYPE).unwrap(), "application/problem+json");
}

#[ntex::test]
async fn create_then_read_update_and_delete() {
    let app = app().await;

    let res = send(&app, Method::POST, "/api/v1/todos", Some(json!({ "title": " milk " }))).await;
    assert_eq!(res.status(), StatusCode::CREATED);
    assert_eq!(res.headers().get(LOCATION).unwrap(), "/api/v1/todos/1");
    assert_eq!(json_body(res).await, json!({ "id": 1, "title": "milk", "done": false }));

    let res = send(&app, Method::PATCH, "/api/v1/todos/1", Some(json!({ "done": true }))).await;
    assert_eq!(json_body(res).await["done"], true);

    let res = send(&app, Method::GET, "/api/v1/todos", None).await;
    assert_eq!(json_body(res).await.as_array().unwrap().len(), 1);

    let res = send(&app, Method::DELETE, "/api/v1/todos/1", None).await;
    assert_eq!(res.status(), StatusCode::NO_CONTENT);
    let res = send(&app, Method::GET, "/api/v1/todos/1", None).await;
    assert_problem(&res, StatusCode::NOT_FOUND);
}

#[ntex::test]
async fn every_failure_is_a_problem() {
    let app = app().await;
    let cases = [
        (Method::GET, "/api/v1/todos/7", None, StatusCode::NOT_FOUND),
        (Method::GET, "/api/v1/todos/abc", None, StatusCode::NOT_FOUND),
        (Method::GET, "/api/v1/nothing", None, StatusCode::NOT_FOUND),
        (Method::GET, "/nothing", None, StatusCode::NOT_FOUND),
        (Method::PUT, "/api/v1/todos", None, StatusCode::METHOD_NOT_ALLOWED),
        (
            Method::POST,
            "/api/v1/todos",
            Some(json!({ "title": "" })),
            StatusCode::UNPROCESSABLE_ENTITY,
        ),
        (
            Method::POST,
            "/api/v1/todos",
            Some(json!({ "name": "x" })),
            StatusCode::BAD_REQUEST,
        ),
        (
            Method::POST,
            "/api/v1/todos",
            Some(json!({ "title": "x".repeat(20_000) })),
            StatusCode::PAYLOAD_TOO_LARGE,
        ),
    ];
    for (method, uri, body, status) in cases {
        let res = send(&app, method.clone(), uri, body).await;
        assert_problem(&res, status);
    }

    let req = test::TestRequest::post()
        .uri("/api/v1/todos")
        .payload("title=x")
        .to_request();
    assert_problem(&test::call_service(&app, req).await, StatusCode::UNSUPPORTED_MEDIA_TYPE);
}

#[ntex::test]
async fn request_id_is_echoed() {
    let app = app().await;
    let req = test::TestRequest::get()
        .uri("/health")
        .header("x-request-id", "abc")
        .to_request();
    let res = test::call_service(&app, req).await;
    assert_eq!(res.headers().get("x-request-id").unwrap(), "abc");

    let res = send(&app, Method::GET, "/health", None).await;
    assert!(res.headers().contains_key("x-request-id"));
}

#[ntex::test]
async fn over_http() {
    // `test::server` expects an app with `()` state, `map_state_factory` supplies ours.
    let srv =
        test::server(async |_| ntex::service::map_state_factory(AppState::new(Store::default()), todo_ntex::app()));

    let res = srv
        .post("/api/v1/todos")
        .send_json(&json!({ "title": "bread" }))
        .await
        .unwrap();
    assert_eq!(res.status(), StatusCode::CREATED);
    let todo: Value = res.json().await.unwrap();
    assert_eq!(todo["title"], "bread");

    let res = srv.get("/health").send().await.unwrap();
    assert_eq!(res.body().await.unwrap(), "Healthy");
}
