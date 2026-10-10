use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
};

use axum::{
    Json, Router,
    extract::{Path, State},
    http::StatusCode,
    routing::get,
};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Clone, Serialize)]
struct Note {
    id: Uuid,
    subject: String,
    body: String,
    pinned: bool,
}

#[derive(Deserialize)]
struct NewNote {
    subject: String,
    body: String,
}

#[derive(Deserialize)]
struct NoteChanges {
    subject: Option<String>,
    body: Option<String>,
    pinned: Option<bool>,
}

type Db = Arc<Mutex<HashMap<Uuid, Note>>>;

#[tokio::main]
async fn main() {
    let db: Db = Arc::default();
    let app = Router::new()
        .route("/notes", get(list).post(create))
        .route("/notes/{id}", get(read).put(update).delete(remove))
        .with_state(db);

    let port = std::env::var("PORT").unwrap_or_else(|_| "3000".to_string());
    let listener = tokio::net::TcpListener::bind(format!("0.0.0.0:{port}"))
        .await
        .unwrap();
    println!("listening on {port}");
    axum::serve(listener, app).await.unwrap();
}

async fn list(State(db): State<Db>) -> Json<Vec<Note>> {
    let mut notes: Vec<Note> = db.lock().unwrap().values().cloned().collect();
    notes.sort_by(|a, b| b.pinned.cmp(&a.pinned).then(a.subject.cmp(&b.subject)));
    Json(notes)
}

async fn create(
    State(db): State<Db>,
    Json(new): Json<NewNote>,
) -> Result<(StatusCode, Json<Note>), (StatusCode, String)> {
    if new.subject.trim().is_empty() {
        return Err((StatusCode::BAD_REQUEST, "subject is required".to_string()));
    }
    if new.body.len() > 10_000 {
        return Err((StatusCode::BAD_REQUEST, "body too long".to_string()));
    }
    let note = Note {
        id: Uuid::new_v4(),
        subject: new.subject.trim().to_string(),
        body: new.body,
        pinned: false,
    };
    db.lock().unwrap().insert(note.id, note.clone());
    Ok((StatusCode::CREATED, Json(note)))
}

async fn read(
    State(db): State<Db>,
    Path(id): Path<Uuid>,
) -> Result<Json<Note>, (StatusCode, String)> {
    match db.lock().unwrap().get(&id) {
        Some(note) => Ok(Json(note.clone())),
        None => Err((StatusCode::NOT_FOUND, format!("note {id} not found"))),
    }
}

async fn update(
    State(db): State<Db>,
    Path(id): Path<Uuid>,
    Json(changes): Json<NoteChanges>,
) -> Result<Json<Note>, (StatusCode, String)> {
    let mut db = db.lock().unwrap();
    let Some(note) = db.get_mut(&id) else {
        return Err((StatusCode::NOT_FOUND, "no such note".to_string()));
    };
    if let Some(subject) = changes.subject {
        if subject.trim().is_empty() {
            return Err((StatusCode::BAD_REQUEST, "subject is required".to_string()));
        }
        note.subject = subject.trim().to_string();
    }
    if let Some(body) = changes.body {
        note.body = body;
    }
    if let Some(pinned) = changes.pinned {
        note.pinned = pinned;
    }
    Ok(Json(note.clone()))
}

async fn remove(State(db): State<Db>, Path(id): Path<Uuid>) -> StatusCode {
    match db.lock().unwrap().remove(&id) {
        Some(_) => StatusCode::NO_CONTENT,
        None => StatusCode::NOT_FOUND,
    }
}
