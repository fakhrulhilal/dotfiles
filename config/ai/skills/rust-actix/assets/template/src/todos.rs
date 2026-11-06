//! `/api/v1/todos`: handlers are plain async functions registered with `.to(..)`; their arguments are extractors.

use std::{
    collections::BTreeMap,
    sync::{
        Mutex,
        atomic::{AtomicU64, Ordering},
    },
};

use actix_web::{
    HttpResponse, Route, guard,
    web::{self, Data, Json, Path, Query},
};
use serde::{Deserialize, Serialize};

use crate::problem::ApiError;

/// Shared by every worker: registered once as `web::Data` (an `Arc`), so the lock is the only synchronisation.
/// Never hold the guard across an `.await`.
#[derive(Default)]
pub struct TodoStore {
    next_id: AtomicU64,
    todos: Mutex<BTreeMap<u64, Todo>>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Todo {
    pub id: u64,
    pub title: String,
    pub completed: bool,
}

#[derive(Debug, Deserialize)]
pub struct CreateTodo {
    pub title: String,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct UpdateTodo {
    pub title: Option<String>,
    pub completed: Option<bool>,
}

#[derive(Debug, Default, Deserialize)]
pub struct ListQuery {
    #[serde(default)]
    pub completed: Option<bool>,
}

/// GET, which also answers HEAD: `web::get()` matches GET only. actix-http leaves the body out of a HEAD response.
fn get() -> Route {
    web::route().guard(guard::Any(guard::Get()).or(guard::Head()))
}

/// Mounted under `/api/v1` by `lib.rs`. A resource groups the methods of one path; another method gets the
/// resource's built-in 405 with `Allow`, which `problem_details` turns into a problem.
pub fn routes(config: &mut web::ServiceConfig) {
    config.service(
        web::resource("/todos")
            .name("todos")
            .route(get().to(list))
            .route(web::post().to(create)),
    );
    config.service(
        web::resource("/todos/{id}")
            .name("todo")
            .route(get().to(read))
            .route(web::patch().to(update))
            .route(web::delete().to(delete)),
    );
}

fn validate_title(title: &str) -> Result<String, ApiError> {
    let title = title.trim();
    if title.is_empty() {
        return Err(ApiError::invalid("title", "must not be empty"));
    }
    Ok(title.to_owned())
}

fn not_found(id: u64) -> ApiError {
    ApiError::NotFound(format!("Todo {id} does not exist."))
}

async fn list(store: Data<TodoStore>, query: Query<ListQuery>) -> Json<Vec<Todo>> {
    let todos = store.todos.lock().unwrap();
    let matching = todos
        .values()
        .filter(|todo| query.completed.is_none_or(|c| todo.completed == c));
    Json(matching.cloned().collect())
}

async fn create(
    store: Data<TodoStore>,
    request: actix_web::HttpRequest,
    input: Json<CreateTodo>,
) -> Result<HttpResponse, ApiError> {
    let title = validate_title(&input.title)?;
    let id = store.next_id.fetch_add(1, Ordering::Relaxed) + 1;
    let todo = Todo {
        id,
        title,
        completed: false,
    };
    store.todos.lock().unwrap().insert(id, todo.clone());
    // `url_for` builds the URL of a named resource.
    let location = request
        .url_for("todo", [id.to_string()])
        .map_err(|error| ApiError::Internal(error.into()))?;
    Ok(HttpResponse::Created()
        .insert_header(("location", location.path()))
        .json(todo))
}

async fn read(store: Data<TodoStore>, id: Path<u64>) -> Result<Json<Todo>, ApiError> {
    let id = id.into_inner();
    let todo = store.todos.lock().unwrap().get(&id).cloned();
    todo.map(Json).ok_or_else(|| not_found(id))
}

async fn update(store: Data<TodoStore>, id: Path<u64>, changes: Json<UpdateTodo>) -> Result<Json<Todo>, ApiError> {
    let id = id.into_inner();
    let changes = changes.into_inner();
    let title = changes.title.as_deref().map(validate_title).transpose()?;

    let mut todos = store.todos.lock().unwrap();
    let todo = todos.get_mut(&id).ok_or_else(|| not_found(id))?;
    if let Some(title) = title {
        todo.title = title;
    }
    if let Some(completed) = changes.completed {
        todo.completed = completed;
    }
    Ok(Json(todo.clone()))
}

async fn delete(store: Data<TodoStore>, id: Path<u64>) -> Result<HttpResponse, ApiError> {
    let id = id.into_inner();
    match store.todos.lock().unwrap().remove(&id) {
        Some(_) => Ok(HttpResponse::NoContent().finish()),
        None => Err(not_found(id)),
    }
}
