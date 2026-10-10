//! A todo list as a JSON API, with OpenAPI docs.

pub mod api;
pub mod cli;
pub mod config;
pub mod domain;
pub mod logging;
pub mod store;

use std::time::Instant;

use anyhow::Context;
use axum::{
    Router,
    extract::Request,
    middleware::{self, Next},
    response::Response,
};
use tokio::net::TcpListener;

use crate::{config::Config, store::Store};

/// The whole application. Tests drive this without a network listener's
/// configuration, so everything a request passes through belongs here.
pub fn app(store: Store) -> Router {
    api::router(store).layer(middleware::from_fn(log_request))
}

pub async fn run(config: Config) -> anyhow::Result<()> {
    let store = store::connect(&config.database)
        .await
        .context("failed to open the database")?;
    let app = app(store);

    let address = format!("{}:{}", config.server.host, config.server.port);
    let listener = TcpListener::bind(&address)
        .await
        .with_context(|| format!("failed to listen on {address}"))?;
    tracing::info!(
        address = %listener.local_addr()?,
        env = ?config.env,
        "server started"
    );

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await?;
    tracing::info!("server stopped");
    Ok(())
}

/// One log line per request. Probes log at debug so they do not drown the rest.
async fn log_request(request: Request, next: Next) -> Response {
    let method = request.method().clone();
    let path = request.uri().path().to_owned();
    let started = Instant::now();

    let response = next.run(request).await;

    let status = response.status().as_u16();
    let latency_ms = started.elapsed().as_micros() as f64 / 1000.0;
    if path.starts_with("/health/") {
        tracing::debug!(%method, path, status, latency_ms, "request");
    } else {
        tracing::info!(%method, path, status, latency_ms, "request");
    }
    response
}

/// Resolves on Ctrl+C, or SIGTERM on Unix.
async fn shutdown_signal() {
    let ctrl_c = async {
        tokio::signal::ctrl_c().await.ok();
    };

    #[cfg(unix)]
    let terminate = async {
        match tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate()) {
            Ok(mut signal) => {
                signal.recv().await;
            }
            Err(_) => std::future::pending().await,
        }
    };
    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        () = ctrl_c => {}
        () = terminate => {}
    }
}
