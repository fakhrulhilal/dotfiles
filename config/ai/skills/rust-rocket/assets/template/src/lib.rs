//! A todo JSON API on Rocket 0.5: Rocket's configuration built in code, managed state opened by an ignite fairing,
//! RFC 9457 problems from responders and catchers, 405 with `Allow`, a CORS fairing, a request log fairing.

pub mod problem;
pub mod todos;

use std::{io::Cursor, net::IpAddr, time::Instant};

use rocket::{
    Build, Request, Response, Rocket,
    config::{Ident, LogLevel, Shutdown},
    data::{Data, Limits, ToByteUnit},
    error::ErrorKind,
    fairing::{AdHoc, Fairing, Info, Kind},
    http::{Header, Method, Status},
};

/// What the app is configured with. In a real app this comes from figment (YAML, environment, arguments).
#[derive(Debug, Clone)]
pub struct Settings {
    pub address: String,
    pub port: u16,
    /// Comma-separated origins, or `*`.
    pub cors_origins: String,
}

/// The whole app, shared by `main` and the tests.
pub fn rocket(settings: &Settings) -> Result<Rocket<Build>, String> {
    let address: IpAddr = settings
        .address
        .parse()
        .map_err(|_| format!("invalid address `{}`: Rocket needs an IP address", settings.address))?;
    let cors = Cors::parse(&settings.cors_origins)?;

    // Built from `Config::default()`, not `Config::figment()`: no Rocket.toml, no ROCKET_* variables.
    let config = rocket::Config {
        address,
        port: settings.port,
        limits: Limits::default().limit("json", 2.mebibytes()),
        ident: Ident::none(),
        // Rocket's own logger stays off; `RequestLog` writes one line per request.
        log_level: LogLevel::Off,
        cli_colors: false,
        // SIGTERM and Ctrl+C: `grace` for open requests, then `mercy` for connections to close.
        shutdown: Shutdown {
            grace: 6,
            mercy: 2,
            ..Shutdown::default()
        },
        ..rocket::Config::default()
    };

    Ok(rocket::custom(config)
        .attach(AdHoc::try_on_ignite("Open the store", |rocket| async move {
            // Where a real app connects to its database (and fails the launch when it cannot).
            Ok(rocket.manage(todos::Store::default()))
        }))
        .attach(cors)
        .attach(RequestLog)
        .mount("/api/v1", todos::routes())
        .register("/", problem::catchers()))
}

/// Reads what went wrong out of a launch error; its `Display` says little.
pub fn launch_error(error: &rocket::Error) -> String {
    match error.kind() {
        ErrorKind::Bind(error) => format!("failed to listen: {error}"),
        ErrorKind::Config(error) => format!("invalid configuration: {error}"),
        ErrorKind::FailedFairings(fairings) => {
            let names: Vec<&str> = fairings.iter().map(|info| info.name).collect();
            format!("failed to start: {}", names.join(", "))
        }
        ErrorKind::Collisions(_) => "routes collide".to_owned(),
        _ => error.to_string(),
    }
}

/// CORS as tower-http does it: a preflight is an empty 200 whatever the route; an origin not listed gets no
/// `Access-Control-Allow-Origin`.
#[derive(Debug, Clone)]
pub enum Cors {
    Any,
    Only(Vec<String>),
}

impl Cors {
    pub fn parse(origins: &str) -> Result<Self, String> {
        let origins: Vec<&str> = origins
            .split(',')
            .map(|origin| origin.trim().trim_end_matches('/'))
            .filter(|o| !o.is_empty())
            .collect();
        if origins.contains(&"*") {
            return Ok(Self::Any);
        }
        origins
            .into_iter()
            .map(|origin| match origin.split_once("://") {
                Some((scheme, host)) if !scheme.is_empty() && !host.is_empty() && !host.contains(['/', ' ']) => {
                    Ok(origin.to_owned())
                }
                _ => Err(format!("invalid CORS origin `{origin}`")),
            })
            .collect::<Result<_, _>>()
            .map(Self::Only)
    }
}

#[rocket::async_trait]
impl Fairing for Cors {
    fn info(&self) -> Info {
        Info {
            name: "CORS",
            kind: Kind::Response,
        }
    }

    async fn on_response<'r>(&self, request: &'r Request<'_>, response: &mut Response<'r>) {
        response.set_header(Header::new(
            "Vary",
            "origin, access-control-request-method, access-control-request-headers",
        ));
        let Some(origin) = request.headers().get_one("Origin") else {
            return;
        };
        if request.method() == Method::Options && request.headers().contains("Access-Control-Request-Method") {
            // Routing made a 405 or 404 of it: turn it into the preflight answer.
            response.set_status(Status::Ok);
            response.remove_header("Content-Type");
            response.remove_header("Allow");
            response.set_sized_body(0, Cursor::new(Vec::new()));
            response.set_header(Header::new("Access-Control-Allow-Methods", "GET,POST,PATCH,DELETE"));
            response.set_header(Header::new("Access-Control-Allow-Headers", "content-type"));
        }
        let allowed = match self {
            Self::Any => Some("*".to_owned()),
            Self::Only(origins) => origins
                .iter()
                .any(|allowed| allowed == origin)
                .then(|| origin.to_owned()),
        };
        if let Some(allowed) = allowed {
            response.set_header(Header::new("Access-Control-Allow-Origin", allowed));
        }
    }
}

/// One log line per request, timed with the request-local cache.
struct RequestLog;
struct Started(Instant);

#[rocket::async_trait]
impl Fairing for RequestLog {
    fn info(&self) -> Info {
        Info {
            name: "Request log",
            kind: Kind::Request | Kind::Response,
        }
    }

    async fn on_request(&self, request: &mut Request<'_>, _: &mut Data<'_>) {
        request.local_cache(|| Started(Instant::now()));
    }

    async fn on_response<'r>(&self, request: &'r Request<'_>, response: &mut Response<'r>) {
        let elapsed = request.local_cache(|| Started(Instant::now())).0.elapsed();
        tracing::info!(
            method = %request.method(),
            path = %request.uri().path(),
            status = response.status().code,
            latency_ms = elapsed.as_micros() as f64 / 1000.0,
            "request"
        );
    }
}
