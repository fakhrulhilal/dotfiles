# State, fairings, CORS and databases

## Managed state

```rust
rocket::build().manage(AppState::new(store))           // once, at build or in an ignite fairing

#[rocket::get("/todos")]
async fn list(state: &State<AppState>) -> Json<Vec<Todo>> { .. }
```

- One value per type, shared by every request (`T: Send + Sync + 'static`). Interior mutability is yours: atomics,
  `RwLock`, or a pool that is already `Sync`. Never hold a `std::sync` guard across `.await`.
- `&State<T>` is a **sentinel**: if a mounted route uses `&State<T>` and nothing managed `T`, launch aborts
  (`ErrorKind::SentinelAborts`) instead of failing at request time.
- Outside a handler: `rocket.state::<T>()` on a `Rocket<Build|Ignite|Orbit>`.

## Fairings

| Callback       | When                                   | Can                                              |
|----------------|----------------------------------------|--------------------------------------------------|
| `on_ignite`    | `ignite()`/`launch()`, before serving  | change the `Rocket` (manage, mount), fail the launch |
| `on_liftoff`   | after the socket is bound              | read config (`rocket.config().port` is the bound port), spawn tasks |
| `on_request`   | each request, before routing           | change method, URI, headers; **cannot answer**   |
| `on_response`  | each response, before it is sent       | change status, headers, body                     |
| `on_shutdown`  | when shutdown starts                   | clean up; they run concurrently                  |

- Attached fairings run in attach order for each callback. The last `on_response` sees what the client gets: attach
  the request log last.
- `AdHoc::on_ignite`, `try_on_ignite`, `on_liftoff`, `on_request`, `on_response`, `on_shutdown`, `config::<T>()`
  (extracts `T` from the figment and manages it).
- Rocket attaches **Shield** itself: `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`,
  `Permissions-Policy: interest-cohort=()`, on every response that does not set them *(run)*. Attach
  `Shield::new()` (no policies) or your own to change it.

Per-request values (timing, a guard's error) go in the request-local cache, keyed by type:

```rust
struct Started(Instant);
async fn on_request(&self, req: &mut Request<'_>, _: &mut Data<'_>) { req.local_cache(|| Started(Instant::now())); }
async fn on_response<'r>(&self, req: &'r Request<'_>, res: &mut Response<'r>) {
    let elapsed = req.local_cache(|| Started(Instant::now())).0.elapsed();
}
```

## Opening a resource before serving

The Rocket way to open a database, a client or a cache: an ignite fairing that manages it, so the launch fails when
it cannot be opened and handlers never see a half-built app.

```rust
AdHoc::try_on_ignite("Open the database", |rocket| async move {
    let url: String = match rocket.figment().extract_inner("app.database.url") { Ok(u) => u, Err(_) => return Err(rocket) };
    match Store::connect(&url).await {
        Ok(store) => Ok(rocket.manage(store)),
        Err(error) => { tracing::error!(%error, "failed to open the database"); Err(rocket) }
    }
})
```

The launch error only names the fairing (`ErrorKind::FailedFairings`), so log the cause inside it.

## Databases

- [`rocket_db_pools`](https://api.rocket.rs/v0.5/rocket_db_pools/) 0.2.0 (`#[derive(Database)]`, `Connection<Db>`
  guard) supports sqlx **0.7**, deadpool-postgres/redis, mongodb and diesel-async, configured under
  `databases.<name>` in the figment. On sqlx 0.8+ or another driver (Turso, SeaORM), open your own pool in an ignite
  fairing as above and pass it as `&State<T>`.
- `rocket_sync_db_pools` is for blocking drivers (diesel, rusqlite) and runs them on the blocking pool.
- Async drivers (sqlx, deadpool) run on Rocket's tokio runtime directly; no `spawn_blocking` needed.

## CORS

Rocket has no CORS. `rocket_cors` 0.6 (2023) exists; a response fairing is short and matches tower-http:

- On every response: `Vary: origin, access-control-request-method, access-control-request-headers`.
- If `Origin` is listed (or `*` is configured): `Access-Control-Allow-Origin`. Errors carry it too, so a browser can
  read the problem.
- A preflight (`OPTIONS` with `Origin` and `Access-Control-Request-Method`) has been routed by then (to a 405 route or
  the 404 catcher): set 200, drop `Content-Type`, `Allow` and the body, add `Access-Control-Allow-Methods` and
  `-Headers`.

The template's `Cors` fairing does exactly this. `on_request` cannot answer the preflight itself; rewriting the
response in `on_response` is the way.
