//! A todo JSON API on actix-web 4. `app()` builds the whole application; `main.rs` serves it and the tests call it
//! in memory.

pub mod problem;
pub mod todos;

use std::time::Instant;

use actix_cors::Cors;
use actix_web::{
    App, Error, HttpResponse,
    body::MessageBody,
    dev::{ServiceFactory, ServiceRequest, ServiceResponse},
    http::{Method, header},
    middleware::{Next, from_fn},
    web::{self, Data, JsonConfig, PathConfig, QueryConfig},
};

use crate::todos::TodoStore;

/// The application. `HttpServer::new` calls this once per worker thread, so everything built here is per worker;
/// `store` was made once in `main` and is shared (`Data` is an `Arc`).
///
/// Arguments are owned, not borrowed: in edition 2024 the `impl` return type would capture a borrow, and the
/// factory closure could not return the app.
pub fn app(
    store: Data<TodoStore>,
    cors_origins: Vec<String>,
) -> App<
    impl ServiceFactory<
        ServiceRequest,
        Config = (),
        Response = ServiceResponse<impl MessageBody>,
        Error = Error,
        InitError = (),
    >,
> {
    App::new()
        .app_data(store)
        // Extractor configs are app data too: the closest one to the route wins.
        .app_data(
            JsonConfig::default()
                .limit(64 * 1024)
                .error_handler(problem::json_error),
        )
        .app_data(PathConfig::default().error_handler(problem::path_error))
        .app_data(QueryConfig::default().error_handler(problem::query_error))
        .service(web::scope("/api/v1").configure(todos::routes))
        .route(
            "/health",
            web::get().to(|| async { HttpResponse::Ok().body("Healthy") }),
        )
        .default_service(web::to(problem::not_found))
        // `wrap` runs in reverse order of registration: the last one is the outermost.
        .wrap(cors(&cors_origins))
        .wrap(from_fn(problem::problem_details))
        .wrap(from_fn(log_request))
}

/// A request from an origin that is not listed is served without the CORS headers (`block_on_origin_mismatch` is
/// off by default). A preflight it refuses gets a 400. `"*"` must be `allow_any_origin()`: `allowed_origin("*")`
/// makes every worker fail to start.
fn cors(origins: &[String]) -> Cors {
    let cors = Cors::default()
        .allowed_methods([Method::GET, Method::POST, Method::PATCH, Method::DELETE])
        .allowed_header(header::CONTENT_TYPE)
        .max_age(3600);
    if origins.iter().any(|origin| origin == "*") {
        cors.allow_any_origin().send_wildcard()
    } else {
        origins.iter().fold(cors, |cors, origin| cors.allowed_origin(origin))
    }
}

/// One log line per request, with the status of the finished response.
async fn log_request(
    request: ServiceRequest,
    next: Next<impl MessageBody>,
) -> Result<ServiceResponse<impl MessageBody>, Error> {
    let (method, path, started) = (request.method().clone(), request.path().to_owned(), Instant::now());
    let response = next.call(request).await?;
    let latency_ms = started.elapsed().as_secs_f64() * 1000.0;
    tracing::info!(%method, path, status = response.status().as_u16(), latency_ms, "request");
    Ok(response)
}
