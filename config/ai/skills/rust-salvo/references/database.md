# Databases

Salvo has no database layer. Create the pool before the server, inject it into every request with
`affix_state::inject(pool)` (feature `affix-state`), and read it with `depot.get_typed::<Pool>()`. Pools are
`Clone` and cheap to clone; inject the pool, never a connection. For a store behind a trait with PostgreSQL and
SQLite backends, migrations and configuration, follow the `rust-backend` skill's storage layer and swap only the
web layer.

```rust
use salvo::affix_state;

let pool = sqlx::PgPool::connect(&database_url).await?;
let router = Router::new().hoop(affix_state::inject(pool)).push(api_routes());
```

`affix_state::inject` stores by type, so inject distinct types (wrap two pools of the same type in newtypes).
`affix_state::insert(key, value)` stores under a string key instead.

## SQLx

```toml
sqlx = { version = "0.9", features = ["runtime-tokio", "postgres", "macros"] }
```

```rust
use salvo::prelude::*;
use sqlx::PgPool;

fn pool(depot: &Depot) -> &PgPool {
    depot.get_typed::<PgPool>().expect("PgPool is injected with affix_state::inject")   // wiring bug, not a 500
}

#[handler]
async fn show(id: salvo::oapi::extract::PathParam<i64>, depot: &mut Depot) -> Result<Json<User>, ApiError> {
    sqlx::query_as::<_, User>("SELECT id, name FROM users WHERE id = $1")
        .bind(*id)
        .fetch_optional(pool(depot))
        .await?                                         // ApiError: From<sqlx::Error>, logs and renders 500
        .map(Json)
        .ok_or_else(|| ApiError::NotFound(format!("user {}", *id)))
}
```

Transactions: `let mut tx = pool.begin().await?;` then `.execute(&mut *tx)` and `tx.commit().await?`. A
transaction dropped without `commit` rolls back, so an early `?` is safe.

Run migrations in `main` before serving (`sqlx::migrate!().run(&pool).await?`), or in a spawned task with a readiness
flag if they are slow.

## SeaORM

Inject the `DatabaseConnection` the same way and read it with `depot.get_typed::<DatabaseConnection>()`. Pick the
`sea-orm` runtime feature that matches tokio (`runtime-tokio-rustls` or similar for the version in use).

## Diesel and other blocking drivers

Diesel's connection API is synchronous. Inject an `r2d2` pool and run every query in `spawn_blocking`, or the query
blocks a tokio worker and every request on it:

```rust
type DbPool = diesel::r2d2::Pool<diesel::r2d2::ConnectionManager<diesel::PgConnection>>;

#[handler]
async fn list(depot: &mut Depot) -> Result<Json<Vec<User>>, StatusError> {
    let pool = depot.get_typed::<DbPool>().expect("DbPool injected").clone();
    let users = tokio::task::spawn_blocking(move || {
        let mut conn = pool.get().map_err(|_| ())?;
        users::table.load::<User>(&mut conn).map_err(|_| ())
    })
    .await
    .map_err(|_| StatusError::internal_server_error())?
    .map_err(|_| StatusError::internal_server_error())?;
    Ok(Json(users))
}
```

`diesel-async` avoids the blocking pool.

## Testing against a database

Build the router from a function that takes the pool (template `router(store)`), and pass a test database or
`sqlite::memory:` pool in tests. Give each test its own database or schema so tests can run in parallel.