//! The whole app through Rocket's local client: routing, guards, catchers and fairings, no socket.

use rocket::{
    http::{ContentType, Header, Status},
    local::asynchronous::{Client, LocalResponse},
};
use serde_json::json;
use todo_rocket::{Settings, problem::Problem, todos::Todo};

const WEB_ORIGIN: &str = "https://todos.example";

async fn client() -> Client {
    let settings = Settings {
        address: "127.0.0.1".into(),
        port: 0,
        cors_origins: WEB_ORIGIN.into(),
    };
    // `tracked` keeps cookies between requests; `untracked` is enough for an API.
    Client::untracked(todo_rocket::rocket(&settings).unwrap())
        .await
        .unwrap()
}

async fn problem(response: LocalResponse<'_>) -> Problem {
    assert_eq!(
        response.content_type(),
        Some(ContentType::new("application", "problem+json"))
    );
    response.into_json().await.unwrap()
}

#[rocket::async_test]
async fn lifecycle() {
    let client = client().await;
    let created = client
        .post("/api/v1/todos")
        .json(&json!({ "title": " Buy milk " }))
        .dispatch()
        .await;
    assert_eq!(created.status(), Status::Created);
    let location = created.headers().get_one("location").unwrap().to_owned();
    let milk: Todo = created.into_json().await.unwrap();
    assert_eq!(milk.title, "Buy milk");
    assert_eq!(location, format!("/api/v1/todos/{}", milk.id));

    let done: Todo = client
        .patch(&location)
        .json(&json!({ "completed": true }))
        .dispatch()
        .await
        .into_json()
        .await
        .unwrap();
    assert!(done.completed);
    let active: Vec<Todo> = client
        .get("/api/v1/todos?status=active")
        .dispatch()
        .await
        .into_json()
        .await
        .unwrap();
    assert!(active.is_empty());

    assert_eq!(client.delete(&location).dispatch().await.status(), Status::NoContent);
    let gone = problem(client.delete(&location).dispatch().await).await;
    assert_eq!(gone.status, 404);
    assert_eq!(gone.instance.as_deref(), Some(location.as_str()));
}

#[rocket::async_test]
async fn every_error_is_a_problem() {
    let client = client().await;
    let todos = "/api/v1/todos";

    let body = problem(client.post(todos).json(&json!({ "title": "" })).dispatch().await).await;
    assert_eq!(
        (body.status, body.errors["title"][0].as_str()),
        (422, "must not be empty")
    );
    assert_eq!(
        problem(
            client
                .post(todos)
                .header(ContentType::JSON)
                .body("{bad")
                .dispatch()
                .await
        )
        .await
        .status,
        400
    );
    assert_eq!(
        problem(client.post(todos).json(&json!({ "name": "x" })).dispatch().await)
            .await
            .status,
        422
    );
    assert_eq!(
        problem(client.post(todos).header(ContentType::Plain).body("x").dispatch().await)
            .await
            .status,
        415
    );
    let large = format!("{{\"title\":\"{}\"}}", "x".repeat(3 * 1024 * 1024));
    assert_eq!(
        problem(
            client
                .post(todos)
                .header(ContentType::JSON)
                .body(large)
                .dispatch()
                .await
        )
        .await
        .status,
        413
    );
    assert_eq!(
        problem(client.get(format!("{todos}/not-a-uuid")).dispatch().await)
            .await
            .status,
        400
    );
    let query = problem(client.get(format!("{todos}?status=sometimes")).dispatch().await).await;
    assert_eq!((query.status, query.instance.as_deref()), (400, Some(todos)));
    assert_eq!(
        problem(client.get("/nothing").dispatch().await).await.detail.as_deref(),
        Some("No such endpoint.")
    );

    let wrong_method = client.put(todos).dispatch().await;
    assert_eq!(wrong_method.headers().get_one("allow"), Some("GET,HEAD,POST"));
    assert_eq!(problem(wrong_method).await.status, 405);
}

#[rocket::async_test]
async fn head_and_paths() {
    let client = client().await;
    let head = client.head("/api/v1/todos").dispatch().await;
    assert_eq!(head.status(), Status::Ok);
    assert!(head.into_bytes().await.unwrap_or_default().is_empty());
    // Rocket matches non-empty segments: a trailing or doubled slash reaches the same route.
    assert_eq!(client.get("/api/v1/todos/").dispatch().await.status(), Status::Ok);
    assert_eq!(client.get("//api//v1/todos").dispatch().await.status(), Status::Ok);
}

#[rocket::async_test]
async fn cors() {
    let client = client().await;
    let preflight = client
        .options("/api/v1/todos")
        .header(Header::new("origin", WEB_ORIGIN))
        .header(Header::new("access-control-request-method", "POST"))
        .dispatch()
        .await;
    assert_eq!(preflight.status(), Status::Ok);
    assert_eq!(
        preflight.headers().get_one("access-control-allow-origin"),
        Some(WEB_ORIGIN)
    );

    let stranger = client
        .get("/api/v1/todos")
        .header(Header::new("origin", "https://elsewhere.example"))
        .dispatch()
        .await;
    assert_eq!(stranger.headers().get_one("access-control-allow-origin"), None);
}
