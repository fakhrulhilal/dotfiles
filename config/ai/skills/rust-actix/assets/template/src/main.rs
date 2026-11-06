use std::{env, io};

use actix_web::{HttpServer, web::Data};
use todo_actix::{app, todos::TodoStore};
use tracing_subscriber::EnvFilter;

// Starts the actix runtime (tokio, current-thread) on the main thread. Each worker gets a runtime of its own.
#[actix_web::main]
async fn main() -> io::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")))
        .init();

    let port: u16 = env::var("PORT").ok().and_then(|port| port.parse().ok()).unwrap_or(8080);
    let origins: Vec<String> = env::var("CORS_ORIGINS")
        .unwrap_or_else(|_| "*".to_owned())
        .split(',')
        .map(|origin| origin.trim().trim_end_matches('/').to_owned())
        .collect();

    // Made once, outside the factory closure, and cloned into every worker's app.
    let store = Data::new(TodoStore::default());

    let server = HttpServer::new(move || app(store.clone(), origins.clone()))
        // SIGTERM stops gracefully (open requests get this long); SIGINT and SIGQUIT stop at once.
        .shutdown_timeout(8)
        .bind(("0.0.0.0", port))?;
    tracing::info!(addresses = ?server.addrs(), "listening");
    server.run().await
}
