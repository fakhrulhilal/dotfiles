//! RFC 9457 problem details for every error, the Rocket way:
//!
//! - `ApiError` and `Problem` are `Responder`s: a handler returns `Result<T, ApiError>`, and the responder sees the
//!   request, so it fills in `instance`.
//! - A guard that fails leaves its problem in the request-local cache (`Rejection::keep`) and fails with the status;
//!   the catcher for that status renders it. Catchers get the status and the request, never the guard's error.
//! - Rocket has no 405 (another method on a known path is a 404): `method_not_allowed` adds one route per other
//!   method that answers 405 with `Allow`.
//! - `Json<T>` wraps Rocket's `Json` guard (400 / 422 / 413 already) and adds 415 for another content type.

use std::{collections::BTreeMap, io::Cursor};

use rocket::{
    Catcher, Data, Request, Response, Route,
    data::{self, FromData},
    http::{ContentType, Header, Method, Status},
    response::{self, Responder},
    route::{self, Handler},
    serde::json,
};
use serde::{Deserialize, Serialize, de::DeserializeOwned};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Problem {
    #[serde(rename = "type")]
    pub type_uri: String,
    pub title: String,
    pub status: u16,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub detail: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub instance: Option<String>,
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub errors: BTreeMap<String, Vec<String>>,
}

impl Problem {
    pub fn new(status: Status) -> Self {
        Self {
            type_uri: "about:blank".to_owned(),
            title: status.reason().unwrap_or("Unknown Error").to_owned(),
            status: status.code,
            detail: None,
            instance: None,
            errors: BTreeMap::new(),
        }
    }

    pub fn detail(mut self, detail: impl Into<String>) -> Self {
        self.detail = Some(detail.into());
        self
    }
}

impl<'r> Responder<'r, 'static> for Problem {
    fn respond_to(self, request: &'r Request<'_>) -> response::Result<'static> {
        let status = Status::from_code(self.status).unwrap_or(Status::InternalServerError);
        let problem = Self {
            instance: Some(request.uri().path().to_string()),
            ..self
        };
        let body = serde_json::to_vec(&problem).unwrap_or_default();
        Response::build()
            .status(status)
            .header(ContentType::new("application", "problem+json"))
            .sized_body(body.len(), Cursor::new(body))
            .ok()
    }
}

#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("{0}")]
    NotFound(String),
    #[error("validation failed")]
    Validation(BTreeMap<String, Vec<String>>),
    #[error("{1}")]
    Rejected(Status, String),
}

impl ApiError {
    pub fn invalid(member: &str, detail: impl Into<String>) -> Self {
        Self::Validation(BTreeMap::from([(member.to_owned(), vec![detail.into()])]))
    }

    pub fn status(&self) -> Status {
        match self {
            Self::NotFound(_) => Status::NotFound,
            Self::Validation(_) => Status::UnprocessableEntity,
            Self::Rejected(status, _) => *status,
        }
    }

    pub fn into_problem(self) -> Problem {
        match self {
            Self::NotFound(detail) => Problem::new(Status::NotFound).detail(detail),
            Self::Validation(errors) => Problem {
                errors,
                ..Problem::new(Status::UnprocessableEntity).detail("One or more members of the request are invalid.")
            },
            Self::Rejected(status, detail) => Problem::new(status).detail(detail),
        }
    }
}

impl<'r> Responder<'r, 'static> for ApiError {
    fn respond_to(self, request: &'r Request<'_>) -> response::Result<'static> {
        self.into_problem().respond_to(request)
    }
}

/// A failed guard's problem, kept for the catcher. `local_cache` is keyed by type and keeps the first value.
struct Rejection(Option<Problem>);

impl Rejection {
    fn keep(request: &Request<'_>, error: ApiError) -> Status {
        let status = error.status();
        request.local_cache(|| Self(Some(error.into_problem())));
        status
    }
}

pub fn catchers() -> Vec<Catcher> {
    rocket::catchers![unknown_endpoint, rejected]
}

#[rocket::catch(404)]
fn unknown_endpoint() -> ApiError {
    ApiError::NotFound("No such endpoint.".to_owned())
}

#[rocket::catch(default)]
fn rejected(status: Status, request: &Request<'_>) -> Problem {
    match &request.local_cache(|| Rejection(None)).0 {
        Some(problem) if problem.status == status.code => problem.clone(),
        _ => Problem::new(status),
    }
}

/// 405 routes for `path`, one per method it does not have. Never HEAD when GET is allowed: Rocket answers HEAD
/// with the GET route only when no HEAD route matches.
pub fn method_not_allowed(path: &str, allowed: &[Method]) -> Vec<Route> {
    let mut allow = allowed.to_vec();
    if allow.contains(&Method::Get) && !allow.contains(&Method::Head) {
        allow.insert(1, Method::Head);
    }
    let handler = MethodNotAllowed(allow.iter().map(|method| method.as_str()).collect::<Vec<_>>().join(","));
    [
        Method::Get,
        Method::Head,
        Method::Post,
        Method::Put,
        Method::Patch,
        Method::Delete,
        Method::Options,
        Method::Trace,
        Method::Connect,
    ]
    .into_iter()
    .filter(|method| !allow.contains(method))
    .map(|method| Route::ranked(100, method, path, handler.clone()))
    .collect()
}

#[derive(Clone)]
struct MethodNotAllowed(String);

#[rocket::async_trait]
impl Handler for MethodNotAllowed {
    async fn handle<'r>(&self, request: &'r Request<'_>, _: Data<'r>) -> route::Outcome<'r> {
        match Problem::new(Status::MethodNotAllowed).respond_to(request) {
            Ok(mut response) => {
                response.set_header(Header::new("Allow", self.0.clone()));
                route::Outcome::Success(response)
            }
            Err(status) => route::Outcome::Error(status),
        }
    }
}

/// A JSON body. Rocket's `Json` reads any content type; this one wants JSON (415 otherwise).
pub struct Json<T>(pub T);

#[rocket::async_trait]
impl<'r, T: DeserializeOwned> FromData<'r> for Json<T> {
    type Error = ();

    async fn from_data(request: &'r Request<'_>, data: Data<'r>) -> data::Outcome<'r, Self> {
        let is_json = request.content_type().is_some_and(|content_type| {
            content_type.is_json()
                || (content_type.top() == "application" && content_type.sub().as_str().ends_with("+json"))
        });
        if !is_json {
            let error = ApiError::Rejected(
                Status::UnsupportedMediaType,
                "Expected request with `Content-Type: application/json`".to_owned(),
            );
            return data::Outcome::Error((Rejection::keep(request, error), ()));
        }
        match json::Json::<T>::from_data(request, data).await {
            data::Outcome::Success(json::Json(value)) => data::Outcome::Success(Json(value)),
            data::Outcome::Forward(forward) => data::Outcome::Forward(forward),
            data::Outcome::Error((status, error)) => {
                let detail = match (status, error) {
                    (Status::PayloadTooLarge, _) => "Failed to buffer the request body: length limit exceeded".into(),
                    (_, json::Error::Parse(_, error)) => format!("Failed to read the JSON body: {error}"),
                    (_, json::Error::Io(error)) => format!("Failed to buffer the request body: {error}"),
                };
                data::Outcome::Error((Rejection::keep(request, ApiError::Rejected(status, detail)), ()))
            }
        }
    }
}
