#[ntex::main]
async fn main() -> std::io::Result<()> {
    env_logger::init_from_env(env_logger::Env::new().default_filter_or("info"));
    let addr = std::env::var("ADDR").unwrap_or_else(|_| "127.0.0.1:8080".into());
    todo_ntex::run(&addr).await
}
