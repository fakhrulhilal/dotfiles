use std::{collections::BTreeMap, sync::RwLock};

use rocket::{
    Route, State,
    http::{Method, Status},
    response::status::Created,
    serde::{json::Json, uuid::Error as UuidError},
};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::problem::{self, ApiError, method_not_allowed};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Todo {
    pub id: Uuid,
    pub title: String,
    pub completed: bool,
}

#[derive(Debug, Deserialize)]
pub struct CreateTodo {
    pub title: String,
}

#[derive(Debug, Default, Deserialize)]
pub struct UpdateTodo {
    pub title: Option<String>,
    pub completed: Option<bool>,
}

#[derive(Debug, Clone, Copy, Default, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StatusFilter {
    #[default]
    All,
    Active,
    Completed,
}

/// Managed state: `&State<Store>` in a handler. Never hold the lock across an `.await`.
#[derive(Default)]
pub struct Store(RwLock<BTreeMap<Uuid, Todo>>);

/// Mounted at `/api/v1`. GET answers HEAD; every path ends in its 405 routes.
pub fn routes() -> Vec<Route> {
    let mut routes = rocket::routes![list, create, get, update, delete];
    routes.extend(method_not_allowed("/todos", &[Method::Get, Method::Post]));
    routes.extend(method_not_allowed(
        "/todos/<_>",
        &[Method::Get, Method::Patch, Method::Delete],
    ));
    routes
}

fn title(title: &str) -> Result<String, ApiError> {
    let title = title.trim();
    if title.is_empty() {
        return Err(ApiError::invalid("title", "must not be empty"));
    }
    Ok(title.to_owned())
}

/// A path parameter that did not parse. Taken as `Result`, or Rocket forwards (and ends in a 422 catcher).
fn id(id: Result<Uuid, UuidError>) -> Result<Uuid, ApiError> {
    id.map_err(|error| ApiError::Rejected(Status::BadRequest, format!("Invalid URL: {error}")))
}

fn not_found(id: Uuid) -> ApiError {
    ApiError::NotFound(format!("Todo {id} does not exist."))
}

/// `?status=` as text, read with serde: an unknown value is a 400 naming the choices.
#[rocket::get("/todos?<status>")]
fn list(store: &State<Store>, status: Option<String>) -> Result<Json<Vec<Todo>>, ApiError> {
    let filter = match status.as_deref() {
        None => StatusFilter::All,
        Some(value) => {
            StatusFilter::deserialize(serde::de::value::StrDeserializer::<serde::de::value::Error>::new(value))
                .map_err(|error| {
                    ApiError::Rejected(
                        Status::BadRequest,
                        format!("Failed to deserialize query string: {error}"),
                    )
                })?
        }
    };
    let todos = store.0.read().unwrap();
    Ok(Json(
        todos
            .values()
            .filter(|todo| match filter {
                StatusFilter::All => true,
                StatusFilter::Active => !todo.completed,
                StatusFilter::Completed => todo.completed,
            })
            .cloned()
            .collect(),
    ))
}

#[rocket::post("/todos", data = "<input>")]
fn create(store: &State<Store>, input: problem::Json<CreateTodo>) -> Result<Created<Json<Todo>>, ApiError> {
    let todo = Todo {
        id: Uuid::now_v7(),
        title: title(&input.0.title)?,
        completed: false,
    };
    store.0.write().unwrap().insert(todo.id, todo.clone());
    Ok(Created::new(format!("/api/v1/todos/{}", todo.id)).body(Json(todo)))
}

#[rocket::get("/todos/<id>")]
fn get(store: &State<Store>, id: Result<Uuid, UuidError>) -> Result<Json<Todo>, ApiError> {
    let id = self::id(id)?;
    store
        .0
        .read()
        .unwrap()
        .get(&id)
        .cloned()
        .map(Json)
        .ok_or_else(|| not_found(id))
}

#[rocket::patch("/todos/<id>", data = "<changes>")]
fn update(
    store: &State<Store>,
    id: Result<Uuid, UuidError>,
    changes: problem::Json<UpdateTodo>,
) -> Result<Json<Todo>, ApiError> {
    let id = self::id(id)?;
    let changes = changes.0;
    let new_title = changes.title.as_deref().map(title).transpose()?;
    let mut todos = store.0.write().unwrap();
    let todo = todos.get_mut(&id).ok_or_else(|| not_found(id))?;
    if let Some(new_title) = new_title {
        todo.title = new_title;
    }
    if let Some(completed) = changes.completed {
        todo.completed = completed;
    }
    Ok(Json(todo.clone()))
}

#[rocket::delete("/todos/<id>")]
fn delete(store: &State<Store>, id: Result<Uuid, UuidError>) -> Result<Status, ApiError> {
    let id = self::id(id)?;
    store
        .0
        .write()
        .unwrap()
        .remove(&id)
        .map(|_| Status::NoContent)
        .ok_or_else(|| not_found(id))
}
