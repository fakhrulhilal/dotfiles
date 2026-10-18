use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use salvo::oapi::extract::{JsonBody, PathParam};
use salvo::prelude::*;
use serde::{Deserialize, Serialize};

use crate::problem::{ApiError, FieldErrors};

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, ToSchema)]
pub struct Todo {
    pub id: u64,
    pub title: String,
    pub done: bool,
}

#[derive(Debug, Serialize, Deserialize, ToSchema)]
pub struct NewTodo {
    /// What to do; 1 to 200 characters.
    pub title: String,
}

#[derive(Debug, Default, Serialize, Deserialize, ToSchema)]
pub struct TodoPatch {
    pub title: Option<String>,
    pub done: Option<bool>,
}

/// In-memory storage, shared by every request through `affix_state::inject`. `Clone` is cheap (an `Arc`).
/// Never hold the lock across an `.await`.
#[derive(Clone, Default)]
pub struct Store(Arc<Mutex<Inner>>);

#[derive(Default)]
struct Inner {
    next_id: u64,
    todos: BTreeMap<u64, Todo>,
}

pub(crate) fn router() -> Router {
    Router::with_path("todos")
        .get(list)
        .post(create)
        .push(Router::with_path("{id}").get(show).patch(update).delete(remove))
}

fn store(depot: &Depot) -> &Store {
    // Injected by `router()`; missing means the app was wired wrong, not a client error.
    depot
        .get_typed::<Store>()
        .expect("Store is injected with affix_state::inject")
}

fn validate_title(title: &str, errors: &mut FieldErrors) {
    let len = title.trim().chars().count();
    if len == 0 {
        errors.entry("title").or_default().push("must not be empty");
    } else if len > 200 {
        errors
            .entry("title")
            .or_default()
            .push("must be at most 200 characters");
    }
}

/// List all todos.
#[endpoint(tags("todos"))]
async fn list(depot: &mut Depot) -> Json<Vec<Todo>> {
    let inner = store(depot).0.lock().unwrap();
    Json(inner.todos.values().cloned().collect())
}

/// Get one todo.
#[endpoint(tags("todos"), status_codes(200, 400, 404))]
async fn show(id: PathParam<u64>, depot: &mut Depot) -> Result<Json<Todo>, ApiError> {
    let inner = store(depot).0.lock().unwrap();
    inner.todos.get(&id).cloned().map(Json).ok_or_else(|| not_found(*id))
}

/// Create a todo.
#[endpoint(
    tags("todos"),
    status_codes(201, 400, 422),
    responses((status_code = 201, description = "Created", body = Todo))
)]
async fn create(body: JsonBody<NewTodo>, depot: &mut Depot, res: &mut Response) -> Result<Json<Todo>, ApiError> {
    let new = body.into_inner();
    let mut errors = FieldErrors::new();
    validate_title(&new.title, &mut errors);
    if !errors.is_empty() {
        return Err(ApiError::Validation(errors));
    }

    let mut inner = store(depot).0.lock().unwrap();
    inner.next_id += 1;
    let todo = Todo {
        id: inner.next_id,
        title: new.title.trim().to_owned(),
        done: false,
    };
    inner.todos.insert(todo.id, todo.clone());
    res.status_code(StatusCode::CREATED);
    Ok(Json(todo))
}

/// Change the title or the done flag.
#[endpoint(tags("todos"))]
async fn update(id: PathParam<u64>, body: JsonBody<TodoPatch>, depot: &mut Depot) -> Result<Json<Todo>, ApiError> {
    let patch = body.into_inner();
    let mut errors = FieldErrors::new();
    if let Some(title) = &patch.title {
        validate_title(title, &mut errors);
    }
    if !errors.is_empty() {
        return Err(ApiError::Validation(errors));
    }

    let mut inner = store(depot).0.lock().unwrap();
    let todo = inner.todos.get_mut(&id).ok_or_else(|| not_found(*id))?;
    if let Some(title) = patch.title {
        todo.title = title.trim().to_owned();
    }
    if let Some(done) = patch.done {
        todo.done = done;
    }
    Ok(Json(todo.clone()))
}

/// Delete a todo.
#[endpoint(tags("todos"), status_codes(204, 400, 404))]
async fn remove(id: PathParam<u64>, depot: &mut Depot) -> Result<StatusCode, ApiError> {
    let mut inner = store(depot).0.lock().unwrap();
    inner
        .todos
        .remove(&id)
        .map(|_| StatusCode::NO_CONTENT)
        .ok_or_else(|| not_found(*id))
}

fn not_found(id: u64) -> ApiError {
    ApiError::NotFound(format!("todo {id} does not exist"))
}
