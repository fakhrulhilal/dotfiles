//! The error domain: every failure leaves the API as RFC 9457 `application/problem+json`.
//!
//! `AppState` selects `ApiError` as its domain, which replaces ntex's `DefaultError` renderers. Every error type the
//! application can produce needs a `WebResponseError<AppState, ApiError>` impl, or the route does not compile:
//! handler errors, extractor errors (`JsonPayloadError`, `PathError`, `QueryPayloadError`, ...) and responder
//! errors (`serde_json::Error` for a `Json<T>` response).
use ntex::http::StatusCode;
use ntex::http::header::CONTENT_TYPE;
use ntex::web::error::{JsonPayloadError, PathError};
use ntex::web::{HttpRequest, HttpResponse, WebResponseError};
use serde::Serialize;

use crate::AppState;

#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("todo {0} does not exist")]
    NotFound(u64),
    #[error("{0}")]
    Invalid(&'static str),
}

#[derive(Serialize)]
struct Problem {
    #[serde(rename = "type")]
    kind: &'static str,
    title: &'static str,
    status: u16,
    detail: String,
}

pub fn problem(status: StatusCode, detail: impl Into<String>) -> HttpResponse {
    HttpResponse::builder(status)
        .header(CONTENT_TYPE, "application/problem+json")
        .json(&Problem {
            kind: "about:blank",
            title: status.canonical_reason().unwrap_or_default(),
            status: status.as_u16(),
            detail: detail.into(),
        })
}

impl WebResponseError<AppState, ApiError> for ApiError {
    fn error_response(&self, _: &AppState) -> HttpResponse {
        let status = match self {
            Self::NotFound(_) => StatusCode::NOT_FOUND,
            Self::Invalid(_) => StatusCode::UNPROCESSABLE_ENTITY,
        };
        problem(status, self.to_string())
    }
}

/// `Json<T>` extractor failures.
impl WebResponseError<AppState, ApiError> for JsonPayloadError {
    fn error_response(&self, _: &AppState) -> HttpResponse {
        match self {
            Self::Overflow => problem(StatusCode::PAYLOAD_TOO_LARGE, "the request body is too large"),
            Self::ContentType => problem(StatusCode::UNSUPPORTED_MEDIA_TYPE, "expected an application/json body"),
            _ => problem(StatusCode::BAD_REQUEST, self.to_string()),
        }
    }
}

/// `Path<T>` extractor failures, such as `/todos/abc` for a numeric id.
impl WebResponseError<AppState, ApiError> for PathError {
    fn error_response(&self, _: &AppState) -> HttpResponse {
        problem(StatusCode::NOT_FOUND, self.to_string())
    }
}

/// `Json<T>` responder failures. Never expected, and the detail stays generic on purpose.
impl WebResponseError<AppState, ApiError> for serde_json::Error {
    fn error_response(&self, _: &AppState) -> HttpResponse {
        log::error!("serializing a response failed: {self}");
        problem(
            StatusCode::INTERNAL_SERVER_ERROR,
            "the response could not be serialized",
        )
    }
}

/// Default service of the app and of each scope: no resource matched the path.
pub async fn not_found(req: HttpRequest) -> HttpResponse {
    problem(StatusCode::NOT_FOUND, format!("no resource at {}", req.path()))
}

/// Default service of each resource: the path matched, the method did not.
pub async fn method_not_allowed(req: HttpRequest) -> HttpResponse {
    problem(
        StatusCode::METHOD_NOT_ALLOWED,
        format!("{} is not supported here", req.method()),
    )
}
