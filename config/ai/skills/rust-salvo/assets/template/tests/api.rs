use salvo::http::header::CONTENT_TYPE;
use salvo::prelude::*;
use salvo::test::{ResponseExt, TestClient};
use serde_json::{Value, json};
use todo_salvo::{Store, Todo, service};

const BASE: &str = "http://127.0.0.1:8080";

fn app() -> Service {
    service(Store::default())
}

fn url(path: &str) -> String {
    format!("{BASE}{path}")
}

/// Asserts an `application/problem+json` response with `status` and returns its body.
async fn problem(res: &mut Response, status: StatusCode) -> Value {
    assert_eq!(res.status_code, Some(status));
    let content_type = res.headers().get(CONTENT_TYPE).unwrap().to_str().unwrap().to_owned();
    assert!(
        content_type.starts_with("application/problem+json"),
        "content type was {content_type}"
    );
    let body: Value = res.take_json().await.unwrap();
    assert_eq!(body["status"], status.as_u16());
    body
}

async fn create(app: &Service, title: &str) -> Todo {
    let mut res = TestClient::post(url("/api/v1/todos"))
        .json(&json!({ "title": title }))
        .send(app)
        .await;
    assert_eq!(res.status_code, Some(StatusCode::CREATED));
    res.take_json().await.unwrap()
}

#[tokio::test]
async fn crud_round_trip() {
    let app = app();

    let todo = create(&app, "  write tests ").await;
    assert_eq!(todo.title, "write tests");
    assert!(!todo.done);

    let mut res = TestClient::patch(url(&format!("/api/v1/todos/{}", todo.id)))
        .json(&json!({ "done": true }))
        .send(&app)
        .await;
    assert_eq!(res.status_code, Some(StatusCode::OK));
    let updated: Todo = res.take_json().await.unwrap();
    assert!(updated.done);

    let list: Vec<Todo> = TestClient::get(url("/api/v1/todos"))
        .send(&app)
        .await
        .take_json()
        .await
        .unwrap();
    assert_eq!(list, vec![updated]);

    let res = TestClient::delete(url(&format!("/api/v1/todos/{}", todo.id)))
        .send(&app)
        .await;
    assert_eq!(res.status_code, Some(StatusCode::NO_CONTENT));
}

#[tokio::test]
async fn missing_todo_is_a_problem_with_instance() {
    let mut res = TestClient::get(url("/api/v1/todos/42?x=1")).send(&app()).await;

    let body = problem(&mut res, StatusCode::NOT_FOUND).await;
    assert_eq!(body["detail"], "todo 42 does not exist");
    assert_eq!(body["instance"], "/api/v1/todos/42");
}

#[tokio::test]
async fn invalid_title_lists_field_errors() {
    let mut res = TestClient::post(url("/api/v1/todos"))
        .json(&json!({ "title": " " }))
        .send(&app())
        .await;

    let body = problem(&mut res, StatusCode::UNPROCESSABLE_ENTITY).await;
    assert_eq!(body["errors"]["title"], json!(["must not be empty"]));
}

#[tokio::test]
async fn malformed_json_is_a_problem() {
    let mut res = TestClient::post(url("/api/v1/todos"))
        .raw_json("{\"title\":")
        .send(&app())
        .await;

    let body = problem(&mut res, StatusCode::BAD_REQUEST).await;
    assert_eq!(body["instance"], "/api/v1/todos");
}

#[tokio::test]
async fn non_numeric_id_is_a_problem() {
    let mut res = TestClient::get(url("/api/v1/todos/abc")).send(&app()).await;

    problem(&mut res, StatusCode::BAD_REQUEST).await;
}

#[tokio::test]
async fn unknown_path_is_a_problem() {
    let mut res = TestClient::get(url("/nope")).send(&app()).await;

    let body = problem(&mut res, StatusCode::NOT_FOUND).await;
    assert_eq!(body["instance"], "/nope");
}

#[tokio::test]
async fn wrong_method_is_a_problem() {
    let mut res = TestClient::put(url("/api/v1/todos")).send(&app()).await;

    problem(&mut res, StatusCode::METHOD_NOT_ALLOWED).await;
}

#[tokio::test]
async fn openapi_documents_the_endpoints() {
    let doc: Value = TestClient::get(url("/api/openapi.json"))
        .send(&app())
        .await
        .take_json()
        .await
        .unwrap();

    let get = &doc["paths"]["/api/v1/todos/{id}"]["get"];
    assert!(get["responses"]["200"].is_object(), "{get}");
    assert!(
        get["responses"]["404"]["content"]["application/problem+json"].is_object(),
        "{get}"
    );
    let create = &doc["paths"]["/api/v1/todos"]["post"]["responses"];
    for status in ["201", "400", "422"] {
        assert!(create[status].is_object(), "POST /todos documents {status}: {create}");
    }
    assert!(doc["components"]["schemas"]["Todo"].is_object(), "short schema names");
    assert!(doc["paths"]["/health"]["get"].is_object());
    assert!(
        doc["paths"]["/api/openapi.json"].is_null(),
        "the doc route itself is not documented"
    );
}

#[tokio::test]
async fn health_is_plain_text() {
    let body = TestClient::get(url("/health"))
        .send(&app())
        .await
        .take_string()
        .await
        .unwrap();

    assert_eq!(body, "Healthy");
}

#[tokio::test]
async fn get_routes_do_not_answer_head() {
    let res = TestClient::head(url("/health")).send(&app()).await;

    assert_eq!(res.status_code, Some(StatusCode::METHOD_NOT_ALLOWED));
}
