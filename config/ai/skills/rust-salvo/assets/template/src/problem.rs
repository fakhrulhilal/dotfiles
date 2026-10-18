use std::collections::BTreeMap;

use salvo::http::ResBody;
use salvo::oapi::{self, Components, EndpointOutRegister, Operation, ToSchema};
use salvo::prelude::*;
use serde::Serialize;

/// Field name to messages, the ASP.NET Core `ValidationProblemDetails` shape.
pub type FieldErrors = BTreeMap<&'static str, Vec<&'static str>>;

#[derive(Serialize, ToSchema)]
struct ValidationExtensions {
    errors: BTreeMap<String, Vec<String>>,
}

/// Every error a handler returns. Renders as RFC 9457 problem details with the request path as `instance`.
#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("{0}")]
    NotFound(String),
    #[error("one or more fields are invalid")]
    Validation(FieldErrors),
}

#[async_trait]
impl Writer for ApiError {
    async fn write(self, req: &mut Request, _depot: &mut Depot, res: &mut Response) {
        let instance = req.uri().path().to_owned();
        match self {
            Self::NotFound(detail) => {
                res.render(Problem::new(StatusCode::NOT_FOUND).detail(detail).instance(instance));
            }
            Self::Validation(fields) => {
                let errors = fields
                    .into_iter()
                    .map(|(name, messages)| (name.to_owned(), messages.into_iter().map(String::from).collect()))
                    .collect();
                res.render(
                    Problem::new(StatusCode::UNPROCESSABLE_ENTITY)
                        .detail("one or more fields are invalid")
                        .instance(instance)
                        .with_extensions(ValidationExtensions { errors }),
                );
            }
        }
    }
}

/// Documents the responses `ApiError` can produce, plus the 400 the catcher renders when an extractor rejects the
/// request. Without this impl an endpoint returning `ApiError` does not compile. Narrow the list per endpoint with
/// `status_codes(..)`.
impl EndpointOutRegister for ApiError {
    fn register(components: &mut Components, operation: &mut Operation) {
        let problem = PlainProblem::to_schema(components);
        let validation = Problem::<ValidationExtensions>::to_schema(components);
        operation.responses.insert(
            "400",
            oapi::Response::new("Malformed request").add_content("application/problem+json", problem.clone()),
        );
        operation.responses.insert(
            "404",
            oapi::Response::new("Not found").add_content("application/problem+json", problem),
        );
        operation.responses.insert(
            "422",
            oapi::Response::new("Validation failed").add_content("application/problem+json", validation),
        );
    }
}

/// Catcher goal: every 4xx/5xx response that has no body yet (unknown path, wrong method, extractor failure,
/// `StatusError`, panic) becomes problem details with the request path as `instance`. Like the built-in
/// `ProblemGoal`, but `ProblemGoal` leaves `instance` out. Responses that already have a body are left alone.
#[handler]
pub async fn catch(req: &mut Request, res: &mut Response) {
    let status = res.status_code.unwrap_or(StatusCode::NOT_FOUND);
    if !(status.is_client_error() || status.is_server_error()) || !(res.body.is_none() || res.body.is_error()) {
        return;
    }

    let problem = match &res.body {
        ResBody::Error(error) => PlainProblem::from(error),
        _ => Problem::new(status),
    };
    res.render(problem.instance(req.uri().path()));
}
