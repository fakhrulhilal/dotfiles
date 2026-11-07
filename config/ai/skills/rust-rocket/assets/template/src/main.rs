use std::process::ExitCode;

use todo_rocket::{Settings, launch_error};

// Builds the tokio runtime; its worker count is Rocket's `workers` (ROCKET_WORKERS or Rocket.toml).
#[rocket::main]
async fn main() -> ExitCode {
    tracing_subscriber::fmt()
        // Rocket logs through `log`, which tracing-subscriber forwards: keep its chatter out by default.
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| "info,rocket=off".into()),
        )
        .init();

    let settings = Settings {
        address: std::env::var("HOST").unwrap_or_else(|_| "127.0.0.1".into()),
        port: std::env::var("PORT")
            .ok()
            .and_then(|port| port.parse().ok())
            .unwrap_or(8080),
        cors_origins: std::env::var("CORS_ORIGINS").unwrap_or_else(|_| "*".into()),
    };
    let rocket = match todo_rocket::rocket(&settings) {
        Ok(rocket) => rocket,
        Err(error) => {
            eprintln!("error: {error}");
            return ExitCode::FAILURE;
        }
    };
    match rocket.launch().await {
        Ok(_) => {
            tracing::info!("server stopped");
            ExitCode::SUCCESS
        }
        Err(error) => {
            // Read the error: a `rocket::Error` dropped without being looked at panics.
            eprintln!("error: {}", launch_error(&error));
            ExitCode::FAILURE
        }
    }
}
