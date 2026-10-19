# Browser clients on another origin (CORS)

Needed only when a web page served from a different origin calls the API
(a static site on GitHub Pages, a dev server on another port). Not needed when
a reverse proxy serves the site and the API under one address, or for
server-to-server clients.

Add `tower-http = { version = "0.7.1", features = ["cors"] }` and a setting
`server.cors_origins` (YAML default `"*"`, `--server-cors-origins`,
`SERVER__CORS_ORIGINS`): a comma-separated list of origins, `*`
meaning any. `app` then takes it and can fail on a malformed origin:

```rust
use axum::http::{HeaderValue, Method, header::CONTENT_TYPE};
use tower_http::cors::{AllowOrigin, CorsLayer};

pub fn app(store: Store, cors_origins: &str) -> anyhow::Result<Router> {
    Ok(api::router(store)
        .layer(cors(cors_origins)?)
        .layer(middleware::from_fn(log_request)))
}

fn cors(origins: &str) -> anyhow::Result<CorsLayer> {
    let origins: Vec<&str> = origins
        .split(',')
        .map(|origin| origin.trim().trim_end_matches('/'))
        .filter(|origin| !origin.is_empty())
        .collect();

    let allow_origin = if origins.contains(&"*") {
        AllowOrigin::any()
    } else {
        let origins = origins
            .into_iter()
            .map(|origin| {
                origin
                    .parse::<HeaderValue>()
                    .with_context(|| format!("invalid CORS origin `{origin}`"))
            })
            .collect::<anyhow::Result<Vec<_>>>()?;
        AllowOrigin::list(origins)
    };

    Ok(CorsLayer::new()
        .allow_origin(allow_origin)
        .allow_methods([Method::GET, Method::POST, Method::PATCH, Method::DELETE])
        .allow_headers([CONTENT_TYPE]))
}
```

- The layer sits outside the router so error responses carry the header too;
  otherwise the browser hides the problem body from the client.
- List every method the API uses in `allow_methods`.
- Test it: a preflight (`OPTIONS` with `origin`,
  `access-control-request-method`, `access-control-request-headers`) from an
  allowed origin returns the origin back; a failing request from an allowed
  origin still has the header; a request from another origin does not.
- `*` suits local work. In production, name the site.
