# Configuration, logging, the server and containers

## Rocket's configuration is a figment

`rocket::build()` reads `Config::figment()`: `Config::default()`, then `Rocket.toml` (or `ROCKET_CONFIG`), then
`ROCKET_*` variables, in the profile named by `ROCKET_PROFILE` (`debug`/`release` by default). `rocket::custom(p)`
takes any provider instead.

To configure Rocket from the app's own settings and nothing else, build the `Config` in code and pass it:

```rust
let config = rocket::Config {
    address: "0.0.0.0".parse()?,                       // IpAddr only: "localhost" does not parse
    port: 8080,
    limits: Limits::default().limit("json", 2.mebibytes()),
    ident: Ident::none(),                              // no `Server: Rocket` header
    log_level: LogLevel::Off,                          // Rocket does not install its logger
    cli_colors: false,
    shutdown: Shutdown { grace: 6, mercy: 2, ..Shutdown::default() },
    ..rocket::Config::default()                        // the struct is non-exhaustive: keep this line
};
let figment = Figment::from(config).merge(Serialized::defaults(&app_settings).key("app"));
rocket::custom(figment)
```

- Put the app's own settings **under a key** (`app`). At the top level, keys named `env` or `log` make Rocket warn
  "found value for deprecated config key `env`" *(run)*.
- Read them back in fairings: `rocket.figment().extract_inner::<DatabaseConfig>("app.database")`, or
  `AdHoc::config::<T>()` to manage the whole struct.
- `workers` is read by `#[rocket::main]` from `Config::figment()` (so `ROCKET_WORKERS`), not from the figment passed
  to `custom`. Default: one per CPU.
- Main keys: `address`, `port`, `workers`, `max_blocking` (512), `keep_alive` (5 s), `limits`, `ident`, `ip_header`
  (`X-Real-IP`), `log_level` (`normal` in debug, `critical` in release), `cli_colors`, `temp_dir`, `tls` (feature
  `tls`), `secret_key` (feature `secrets`, required in release), `shutdown`.

## Logging with tracing

- Rocket logs through the `log` crate. `tracing-subscriber`'s `init()`/`try_init()` installs the `log` bridge (its
  default `tracing-log` feature), so **Rocket's lines appear even with `log_level` off** *(run)*: launch banners,
  every route, and per request "Matched", "Outcome", "No matching routes" (at ERROR for every 404).
- Add `rocket=off` to the filter unless the user asks for them (`info,rocket=info`), and log requests with a fairing.
- `log_level: Off` only stops Rocket from installing its own logger; with no bridge and Off, Rocket prints nothing,
  launch errors included: read them from `launch()`'s `Err`.

## Launch and its errors

```rust
match rocket.launch().await {
    Ok(_rocket) => {}                                  // returned after a graceful shutdown
    Err(error) => match error.kind() {                 // reading it marks it handled
        ErrorKind::Bind(e) => ..,                      // address in use, permission
        ErrorKind::Config(e) => ..,                    // a figment error naming the key
        ErrorKind::FailedFairings(infos) => ..,        // names of the ignite fairings that failed
        ErrorKind::Collisions(_) | ErrorKind::SentinelAborts(_) => ..,
        _ => ..,
    },
}
```

A `rocket::Error` that is **dropped without being read panics** (it pretty-prints and aborts). `Display`,
`Debug`, `kind()` and `pretty_print()` all mark it handled; its `Display` is terse ("launch fairing(s) failed").

## Shutdown

- `Shutdown::default()`: `ctrlc: true`, `signals: [term]` on Unix, `grace: 2`, `mercy: 3`, `force: true`.
- After a signal: open requests get `grace` seconds, then connections get `mercy` seconds to close; `force` then
  ends the remaining tasks. Keep `grace + mercy` under Docker's 10 s stop timeout.
- In code: the `Shutdown` request guard, or `rocket.shutdown()` on an `Orbit` (in `on_liftoff`), returns a handle
  whose `notify()` starts it.

## Runtime

Rocket 0.5 runs on tokio (multi-threaded). `#[rocket::main]` builds the runtime; `#[launch] fn rocket() -> _` builds
it and launches what the function returns. tokio-based crates (sqlx `runtime-tokio`, reqwest) work unchanged.

## Container

The same recipe as any static Rust binary: cross-compile with `tonistiigi/xx` on the build platform for
`linux/amd64` and `linux/arm64`, link statically against musl (`rust:<v>-alpine`, `xx-apk add musl-dev gcc` for C
code such as ring or Turso), copy the binary into `FROM scratch` with a static health-check client (tiny-hc), run as
a numeric user. Rocket needs nothing else at runtime: no `Rocket.toml`, no templates or static folders unless the
app uses them. Set the address to `0.0.0.0` in the image (`SERVER__HOST` or your equivalent). todo-app's `backend/rocket`
image, with sqlx (PostgreSQL) and Turso, is 24.7 MB.
