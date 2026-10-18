//! A todo JSON API on Salvo 1.0: OpenAPI endpoints, RFC 9457 problem details for every error, shared state
//! through the depot, and a health probe.

mod problem;
mod todos;

use salvo::affix_state;
use salvo::catch_panic::CatchPanic;
use salvo::catcher::Catcher;
use salvo::logging::Logger;
use salvo::oapi::naming::{self, FlexNamer};
use salvo::oapi::{Info, OpenApi};
use salvo::prelude::*;

pub use problem::ApiError;
pub use todos::{NewTodo, Store, Todo, TodoPatch};

/// The routes, without docs. `openapi()` reads the endpoints from this tree.
pub fn router(store: Store) -> Router {
    Router::new()
        .hoop(affix_state::inject(store))
        .push(Router::with_path("health").get(health))
        .push(Router::with_path("api/v1").push(todos::router()))
}

/// The OpenAPI document generated from every `#[endpoint]` in `router`.
pub fn openapi(router: &Router) -> OpenApi {
    // Schema names default to full Rust paths (`todo_salvo.todos.Todo`); use type names (`Todo`,
    // `Problem_ValidationExtensions`) instead. The namer is global, so set it before any schema is generated.
    naming::set_namer(FlexNamer::new().short_mode(true).generic_delimiter("_", ""));
    OpenApi::new("Todo API", "0.1.0")
        .info(Info::new("Todo API", "0.1.0").description("A todo list on Salvo."))
        .merge_router(router)
}

/// The complete service: routes, docs, panic guard, request log and a catcher that turns every error without a
/// body (unknown path, wrong method, extractor failures, `StatusError`) into `application/problem+json`.
pub fn service(store: Store) -> Service {
    let router = router(store);
    let doc = openapi(&router);
    let router = router
        .unshift(doc.into_router("/api/openapi.json"))
        .unshift(SwaggerUi::new("/api/openapi.json").into_router("/swagger-ui"));

    Service::new(Router::new().hoop(CatchPanic::new()).push(router))
        .hoop(Logger::new())
        .catcher(Catcher::new(problem::catch))
}

/// Liveness probe.
#[endpoint(tags("health"))]
async fn health() -> &'static str {
    "Healthy"
}
