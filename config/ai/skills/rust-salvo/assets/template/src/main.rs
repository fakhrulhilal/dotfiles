use std::time::Duration;

use salvo::prelude::*;
use salvo::server::ServerHandle;
use todo_salvo::{Store, service};
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")))
        .init();

    let addr = std::env::var("BIND").unwrap_or_else(|_| "0.0.0.0:8080".into());
    // `bind()` panics when the address is taken; `try_bind()` lets us report it and exit non-zero.
    let acceptor = match TcpListener::new(addr.clone()).try_bind().await {
        Ok(acceptor) => acceptor,
        Err(error) => {
            tracing::error!(%addr, %error, "cannot bind");
            std::process::exit(1);
        }
    };
    let server = Server::new(acceptor);
    tokio::spawn(shutdown_on_signal(server.handle()));
    server.serve(service(Store::default())).await;
}

/// Ctrl+C or SIGTERM: stop accepting, then give in-flight requests 25 s (under Kubernetes' 30 s grace period).
async fn shutdown_on_signal(handle: ServerHandle) {
    let ctrl_c = async { tokio::signal::ctrl_c().await.expect("install Ctrl+C handler") };
    #[cfg(unix)]
    let terminate = async {
        use tokio::signal::unix::{SignalKind, signal};
        signal(SignalKind::terminate())
            .expect("install SIGTERM handler")
            .recv()
            .await;
    };
    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {}
        _ = terminate => {}
    }
    tracing::info!("shutting down");
    handle.stop_graceful(Duration::from_secs(25));
}
