//! `/api/v1/todos`.
use ntex::http::StatusCode;
use ntex::web::types::{Json, Path};
use ntex::web::{self, Responder};
use serde::{Deserialize, Serialize};

use crate::AppState;
use crate::middleware::RequestId;
use crate::problem::{ApiError, method_not_allowed};

#[derive(Clone, Serialize)]
pub struct Todo {
    pub id: u64,
    pub title: String,
    pub done: bool,
}

#[derive(Deserialize)]
pub struct NewTodo {
    title: String,
}

#[derive(Deserialize)]
pub struct UpdateTodo {
    title: Option<String>,
    done: Option<bool>,
}

/// `In` = `RequestId`: these routes sit behind the `request_id` filter, so `to_with_state()` handlers take a
/// `RequestId` as their second argument. Registering one anywhere else is a type error.
pub fn routes(cfg: &mut web::ServiceConfig<AppState, RequestId>) {
    // A `resource` groups the methods of one path, so a wrong method reaches its default service (405). With
    // `.route(path, web::get()...)` a wrong method would fall through to the scope's 404 instead.
    cfg.service(
        web::resource("/todos")
            .route(web::get().to_with_state(list))
            .route(web::post().to_with_state(create))
            .default_service(web::to(method_not_allowed)),
    );
    cfg.service(
        web::resource("/todos/{id}")
            .route(web::get().to_with_state(get))
            .route(web::patch().to_with_state(update))
            .route(web::delete().to_with_state(delete))
            .default_service(web::to(method_not_allowed)),
    );
}

// State-aware handlers: `&AppState`, then the request state, then up to 16 extractors.

async fn list(st: &AppState, _: RequestId) -> Json<Vec<Todo>> {
    Json(st.store.lock().unwrap().values().cloned().collect())
}

async fn get(st: &AppState, _: RequestId, id: Path<u64>) -> Result<Json<Todo>, ApiError> {
    let id = id.into_inner();
    st.store
        .lock()
        .unwrap()
        .get(&id)
        .cloned()
        .map(Json)
        .ok_or(ApiError::NotFound(id))
}

// `+ use<>`: in edition 2024 a returned `impl Trait` captures the `&AppState` lifetime, and `to_with_state()` then
// rejects the handler with "HandlerSt is not implemented". The empty capture list says the response borrows nothing.
async fn create(
    st: &AppState,
    RequestId(rid): RequestId,
    body: Json<NewTodo>,
) -> Result<impl Responder<AppState> + use<>, ApiError> {
    let title = valid_title(body.into_inner().title)?;
    // Keep lock guards and `RefCell` borrows out of `.await`: the worker runs other requests in between.
    let todo = {
        let mut store = st.store.lock().unwrap();
        let id = store.last_key_value().map_or(1, |(id, _)| id + 1);
        let todo = Todo { id, title, done: false };
        store.insert(id, todo.clone());
        todo
    };
    log::info!("[{rid}] created todo {}", todo.id);
    let location = format!("/api/v1/todos/{}", todo.id);
    Ok(Json(todo)
        .with_status(StatusCode::CREATED)
        .with_header("location", location))
}

async fn update(st: &AppState, _: RequestId, id: Path<u64>, body: Json<UpdateTodo>) -> Result<Json<Todo>, ApiError> {
    let (id, body) = (id.into_inner(), body.into_inner());
    let title = body.title.map(valid_title).transpose()?;
    let mut store = st.store.lock().unwrap();
    let todo = store.get_mut(&id).ok_or(ApiError::NotFound(id))?;
    if let Some(title) = title {
        todo.title = title;
    }
    if let Some(done) = body.done {
        todo.done = done;
    }
    Ok(Json(todo.clone()))
}

async fn delete(st: &AppState, _: RequestId, id: Path<u64>) -> Result<web::HttpResponse, ApiError> {
    let id = id.into_inner();
    match st.store.lock().unwrap().remove(&id) {
        Some(_) => Ok(web::HttpResponse::NoContent().build()),
        None => Err(ApiError::NotFound(id)),
    }
}

fn valid_title(title: String) -> Result<String, ApiError> {
    match title.trim() {
        "" => Err(ApiError::Invalid("title must not be empty")),
        trimmed => Ok(trimmed.to_owned()),
    }
}
