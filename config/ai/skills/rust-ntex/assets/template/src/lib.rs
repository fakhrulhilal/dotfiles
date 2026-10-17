//! A todo JSON API on ntex 4, running on the compio runtime.
mod middleware;
mod problem;
mod todos;

use std::cell::Cell;
use std::collections::BTreeMap;
use std::io;
use std::rc::Rc;
use std::sync::{Arc, Mutex};

use ntex::error::Failure;
use ntex::http::{HttpServiceConfig, Request, Response};
use ntex::io::IoConfig;
use ntex::service::fn_service_st;
use ntex::time::Seconds;
use ntex::web::types::JsonConfig;
use ntex::web::{self, App, WebAppConfig, WebError};
use ntex::{ServiceFactory, SharedCfg};

pub use problem::ApiError;
pub use todos::Todo;

/// The todos, shared by every worker. Process-wide data needs `Arc` and a lock.
pub type Store = Arc<Mutex<BTreeMap<u64, Todo>>>;

/// Application state. The server creates one value per worker and clones it for every connection, so anything the
/// clones must share sits behind `Rc` (one worker) or `Arc` (all workers).
#[derive(Clone)]
pub struct AppState {
    store: Store,
    /// Requests seen by this worker. It never leaves the worker thread, so `Rc<Cell>` needs no locking.
    requests: Rc<Cell<u64>>,
}

impl AppState {
    pub fn new(store: Store) -> Self {
        Self {
            store,
            requests: Rc::new(Cell::new(0)),
        }
    }
}

/// `Error` selects the error domain: every error that can reach a response must implement
/// `WebResponseError<AppState, ApiError>` (see `problem.rs`).
impl web::State for AppState {
    type Error = ApiError;
}

/// The application, shared by `run()` and the tests.
pub fn app()
-> impl ServiceFactory<AppState, Request, Res = Response, Error = WebError<AppState, ApiError>, InitError = Failure> {
    App::new()
        // Application-wide settings come before the first route or service.
        .with_config(WebAppConfig::new().set_state(JsonConfig::default().limit(16 * 1024)))
        // Middleware runs in registration order on the way in: Logger wraps everything.
        .middleware(web::middleware::Logger::default())
        .middleware(middleware::AssignRequestId)
        // WebRequest<()> -> WebRequest<RequestId>: handlers below receive the id as their request state.
        .filter(fn_service_st(middleware::request_id))
        .service(
            web::scope("/api/v1")
                .configure(todos::routes)
                // A matched scope never falls back to the app's default service.
                .default_service(web::to(problem::not_found)),
        )
        .route("/health", web::get().to(async || "Healthy"))
        .default_service(web::to(problem::not_found))
        .build()
}

/// Serves the API on `addr` until SIGTERM (graceful) or Ctrl-C (immediate).
pub async fn run(addr: &str) -> io::Result<()> {
    // Created once, outside the state factory, so all workers share it.
    let store = Store::default();
    // Connection and protocol settings travel with each accepted connection. Adding them also silences the
    // "Configuration ... does not exist, using default" log lines.
    let cfg = SharedCfg::new("API")
        .add(IoConfig::new().set_shutdown_timeout(Seconds(2)))
        .add(HttpServiceConfig::new().set_keepalive_timeout(Seconds(30)));

    web::server_with_config(
        async move || Ok(AppState::new(store.clone())),
        async |_: &AppState| app(),
    )
    .graceful_shutdown_timeout(Seconds(10))
    .bind(addr, cfg)?
    .run()
    .await
}
