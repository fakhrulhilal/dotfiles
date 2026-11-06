//! Every error as an RFC 9457 problem (`application/problem+json`).
//!
//! - Handlers return `Result<_, ApiError>`; `ApiError` is a `ResponseError`.
//! - Extractor failures go through the `error_handler` of `JsonConfig`, `PathConfig` and `QueryConfig` (see
//!   `lib.rs`), which turn them into `ApiError::BadRequest` and friends.
//! - `ResponseError::error_response` cannot see the request, so `problem_details` (a `from_fn` middleware) sets
//!   `instance` afterwards and turns every other error response (404, 405, CORS refusals) into a problem.

use std::collections::BTreeMap;

use actix_web::{
    Error, HttpRequest, HttpResponse, ResponseError,
    body::{BoxBody, MessageBody},
    dev::{ServiceRequest, ServiceResponse},
    error::{JsonPayloadError, PathError, QueryPayloadError},
    http::{
        StatusCode,
        header::{CONTENT_LENGTH, CONTENT_TYPE},
    },
    middleware::Next,
};
use serde::{Deserialize, Serialize};

pub const PROBLEM_JSON: &str = "application/problem+json";

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Problem {
    #[serde(rename = "type")]
    pub type_uri: String,
    pub title: String,
    pub status: u16,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub detail: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub instance: Option<String>,
    /// Invalid members of the request, by name.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub errors: BTreeMap<String, Vec<String>>,
}

impl Problem {
    pub fn new(status: StatusCode) -> Self {
        Self {
            type_uri: "about:blank".to_owned(),
            title: status.canonical_reason().unwrap_or("Unknown Error").to_owned(),
            status: status.as_u16(),
            detail: None,
            instance: None,
            errors: BTreeMap::new(),
        }
    }

    pub fn detail(mut self, detail: impl Into<String>) -> Self {
        self.detail = Some(detail.into());
        self
    }

    pub fn into_response(self) -> HttpResponse {
        let status = StatusCode::from_u16(self.status).unwrap_or(StatusCode::INTERNAL_SERVER_ERROR);
        let body = serde_json::to_vec(&self).unwrap_or_default();
        let mut response = HttpResponse::build(status).content_type(PROBLEM_JSON).body(body);
        // Kept for `problem_details`, which adds what only the request knows.
        response.extensions_mut().insert(self);
        response
    }
}

#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("{0}")]
    NotFound(String),
    #[error("validation failed")]
    Validation(BTreeMap<String, Vec<String>>),
    /// The request could not be read. The message is shown to the client.
    #[error("{1}")]
    Rejected(StatusCode, String),
    /// Anything unexpected. Logged, never shown.
    #[error(transparent)]
    Internal(#[from] Box<dyn std::error::Error + Send + Sync>),
}

impl ApiError {
    pub fn invalid(member: &str, detail: impl Into<String>) -> Self {
        Self::Validation(BTreeMap::from([(member.to_owned(), vec![detail.into()])]))
    }
}

impl ResponseError for ApiError {
    fn status_code(&self) -> StatusCode {
        match self {
            Self::NotFound(_) => StatusCode::NOT_FOUND,
            Self::Validation(_) => StatusCode::UNPROCESSABLE_ENTITY,
            Self::Rejected(status, _) => *status,
            Self::Internal(_) => StatusCode::INTERNAL_SERVER_ERROR,
        }
    }

    fn error_response(&self) -> HttpResponse {
        let status = self.status_code();
        let problem = match self {
            Self::NotFound(detail) | Self::Rejected(_, detail) => Problem::new(status).detail(detail),
            Self::Validation(errors) => Problem {
                errors: errors.clone(),
                ..Problem::new(status).detail("One or more members of the request are invalid.")
            },
            Self::Internal(error) => {
                tracing::error!(%error, "request failed");
                Problem::new(status).detail("The request could not be completed.")
            }
        };
        problem.into_response()
    }
}

fn rejected(status: StatusCode, detail: String) -> Error {
    ApiError::Rejected(status, detail).into()
}

/// For `JsonConfig::error_handler`. actix answers 400 for all of these by default; these are the statuses most
/// APIs use: 415 for another content type, 422 for JSON of the wrong shape, 413 above the limit.
pub fn json_error(error: JsonPayloadError, _: &HttpRequest) -> Error {
    match error {
        JsonPayloadError::OverflowKnownLength { .. } | JsonPayloadError::Overflow { .. } => {
            rejected(StatusCode::PAYLOAD_TOO_LARGE, error.to_string())
        }
        JsonPayloadError::ContentType => rejected(
            StatusCode::UNSUPPORTED_MEDIA_TYPE,
            "Expected `Content-Type: application/json`".to_owned(),
        ),
        JsonPayloadError::Deserialize(error) if error.is_data() => {
            rejected(StatusCode::UNPROCESSABLE_ENTITY, format!("Invalid JSON body: {error}"))
        }
        // Not JSON at all, cut short, or the body could not be read. The enum is non-exhaustive.
        error => rejected(StatusCode::BAD_REQUEST, error.to_string()),
    }
}

/// For `PathConfig::error_handler`: a path value of the wrong type, such as `/todos/abc`.
pub fn path_error(error: PathError, _: &HttpRequest) -> Error {
    rejected(StatusCode::BAD_REQUEST, error.to_string())
}

/// For `QueryConfig::error_handler`: a query value of the wrong type.
pub fn query_error(error: QueryPayloadError, _: &HttpRequest) -> Error {
    rejected(StatusCode::BAD_REQUEST, error.to_string())
}

/// `App::default_service`: no resource matched the path.
pub async fn not_found() -> HttpResponse {
    ApiError::NotFound("No such endpoint.".to_owned()).error_response()
}

/// Sets `instance` on every problem and turns the remaining error responses into problems: a resource's built-in
/// 405 (bodiless, with `Allow`), and CORS refusals (a text body with the error attached).
pub async fn problem_details(
    request: ServiceRequest,
    next: Next<impl MessageBody + 'static>,
) -> Result<ServiceResponse<BoxBody>, Error> {
    // Never keep `request.request().clone()` across `next.call`: the router needs the only reference to the
    // request and panics otherwise. Everything needed afterwards is on the response.
    let mut response = next.call(request).await?.map_into_boxed_body();

    let status = response.status();
    let stored = response.response_mut().extensions_mut().remove::<Problem>();
    let problem = match stored {
        Some(problem) => problem,
        None if !(status.is_client_error() || status.is_server_error()) => return Ok(response),
        None => match response.response().error() {
            Some(error) if status.is_client_error() => Problem::new(status).detail(error.to_string()),
            Some(_) => Problem::new(status),
            None if response.headers().contains_key(CONTENT_TYPE) => return Ok(response),
            None => Problem::new(status),
        },
    };

    let (request, original) = response.into_parts();
    let mut finished = Problem {
        instance: Some(request.path().to_owned()),
        ..problem
    }
    .into_response();
    // Keep `Allow`, the CORS headers and anything else the original had.
    for (name, value) in original.headers() {
        if name != CONTENT_TYPE && name != CONTENT_LENGTH {
            finished.headers_mut().append(name.clone(), value.clone());
        }
    }
    Ok(ServiceResponse::new(request, finished))
}
