# Security middleware

Each piece is a feature of `salvo` and a hoop. Order on a router: session, then CSRF or flash (they read the
session), then auth, then authorization checks, then the handlers. CORS goes on the `Service` or the outermost
router so preflights and errors get the headers too.

## CORS (`cors`)

`Cors` is a builder; `.into_handler()` makes the hoop.

```rust
use salvo::cors::{AllowOrigin, Any, Cors};
use salvo::http::{HeaderValue, Method};

let cors = Cors::new()
    .allow_origin(["https://app.example.com", "https://admin.example.com"])
    .allow_methods([Method::GET, Method::POST, Method::PATCH, Method::DELETE])
    .allow_headers(vec!["authorization", "content-type"])
    .expose_headers(vec!["x-request-id"])
    .allow_credentials(true)
    .max_age(3600)
    .into_handler();
let service = Service::new(router).hoop(cors);
```

- `allow_origin` takes `&str`, `[&str; N]`, `Vec<&str>`, `HeaderValue`s, `Any`, `AllowOrigin::mirror_request()` or
  `AllowOrigin::dynamic(|origin: Option<&HeaderValue>, req: &Request, depot: &Depot| -> Option<HeaderValue>)`.
- `allow_methods` takes `Method` values (`[Method; N]`, `Vec<Method>`, `Method`) or `Any`; **not** strings.
  `allow_headers` takes strings, `HeaderName`s, `Any` or `AllowHeaders::mirror_request()`.
- `into_handler()` panics on an origin that is not a valid header value, and on `allow_credentials(true)` combined
  with `Any` for origins, methods, headers or exposed headers. Build CORS at startup from validated config.
- `Cors::permissive()` (any origin, no credentials) is for local development; `Cors::very_permissive()` mirrors the
  origin with credentials and logs a warning. Ship neither.

## JWT (`jwt-auth`)

Add `jsonwebtoken = "11"` (the major `salvo-jwt-auth` 1.0.1 uses) to mint tokens; decoding comes from salvo.

```rust
use jsonwebtoken::{EncodingKey, Header, encode};
use salvo::jwt_auth::{ConstDecoder, HeaderFinder, JwtAuth, JwtAuthDepotExt, QueryFinder};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]          // Clone is required by JwtAuth's Handler impl
struct Claims {
    sub: String,
    exp: i64,                                            // checked by the default validation
}

let auth: JwtAuth<Claims, _> = JwtAuth::new(ConstDecoder::from_secret(secret))
    .finders(vec![Box::new(HeaderFinder::new()), Box::new(QueryFinder::new("token"))]);
let router = Router::new()
    .push(Router::with_path("login").post(login))
    .push(Router::with_path("me").hoop(auth).get(me));

fn mint(secret: &[u8], claims: &Claims) -> jsonwebtoken::errors::Result<String> {
    encode(&Header::default(), claims, &EncodingKey::from_secret(secret))
}

#[handler]
async fn me(depot: &mut Depot) -> Result<String, StatusError> {
    let data = depot.jwt_auth_data::<Claims>().ok_or_else(StatusError::unauthorized)?;
    Ok(data.claims.sub.clone())
}
```

- `JwtAuth::new` takes a decoder: `ConstDecoder::from_secret`, `from_rsa_pem`, `from_ec_pem`, or `OidcDecoder` with
  feature `oidc`. It does not take a `&str` secret.
- Finders: `HeaderFinder` (`Authorization: Bearer`, the default), `QueryFinder`, `CookieFinder`, `FormFinder`.
- No token: **401**. A token that fails to decode or validate (bad signature, expired): **403**. Both stop the
  chain. `.force_passed(true)` lets the request through and records `depot.jwt_auth_state()`
  (`Authorized`, `Unauthorized`, `Forbidden`) for the handler to decide.
- `depot.jwt_auth_token()` gives the raw token. Annotate `JwtAuth<Claims, _>` when the claims type cannot be
  inferred.

## Basic auth (`basic-auth`)

```rust
use salvo::basic_auth::{BasicAuth, BasicAuthDepotExt, BasicAuthValidator};

struct Validator;
impl BasicAuthValidator for Validator {                   // native async fn, no #[async_trait]
    async fn validate(&self, username: &str, password: &str, depot: &mut Depot) -> bool {
        check_credentials(username, password).await            // your user store; compare hashes
    }
}

let router = Router::with_path("admin").hoop(BasicAuth::new(Validator).set_realm("admin")).get(admin);
// in a handler: depot.basic_auth_username()
```

## Sessions (`session`)

```rust
use salvo::session::{CookieStore, MemoryStore, Session, SessionDepotExt, SessionHandler};

let session = SessionHandler::builder(CookieStore::new(), &secret_64_bytes)   // panics under 64 bytes; try_new
    .session_ttl(Some(Duration::from_secs(3600)))
    .cookie_name("sid")
    .same_site_policy(SameSite::Lax)
    .secure_cookie_policy(SecureCookiePolicy::Always)     // when TLS ends at a proxy
    .build()
    .unwrap();

#[handler]
async fn login(req: &mut Request, depot: &mut Depot, res: &mut Response) {
    let mut session = Session::new();                     // a fresh session on login prevents fixation
    session.insert("user", req.form::<String>("username").await.unwrap_or_default()).unwrap();
    depot.set_session(session);
    res.render(Redirect::other("/"));
}

#[handler]
async fn home(depot: &mut Depot) -> String {
    match depot.session_mut().and_then(|s| s.get::<String>("user")) {
        Some(user) => format!("Hello, {user}"),
        None => "Please log in".into(),
    }
}
```

- Stores: `CookieStore` (encrypted cookie, about 4 KB), `MemoryStore` (one process, lost on restart). Several
  instances need a shared store.
- Values are `Serialize + Deserialize`. `session.remove(key)` drops one value, `session.destroy()` ends the session
  (logout), `session.regenerate()` keeps the data under a new id. There is no `clear()`.
- Builder: `session_ttl`, `cookie_name`, `cookie_path`, `cookie_domain`, `same_site_policy`, `secure_cookie`,
  `secure_cookie_policy`, `save_unchanged`, `fallback_keys`, `add_fallback_key` (key rotation). `HttpOnly` is
  always on. `Secure` defaults to `AutoFromScheme`, which is wrong behind a TLS-terminating proxy.

Paths: `SameSite` is `salvo::http::cookie::SameSite`; `SecureCookiePolicy` is `salvo::http::SecureCookiePolicy`.

## CSRF (`csrf`)

```rust
use salvo::csrf::{CsrfDepotExt, FormFinder, HeaderFinder, hmac_cookie_csrf};

let csrf = hmac_cookie_csrf(key_32_bytes, FormFinder::new("csrf_token")).add_finder(HeaderFinder::new("x-csrf-token"));
let router = Router::new().hoop(csrf).get(show_form).post(submit);

#[handler]
async fn show_form(depot: &mut Depot) -> Text<String> {
    let token = depot.csrf_token().unwrap_or_default();
    Text::Html(format!(r#"<form method="post"><input type="hidden" name="csrf_token" value="{token}"></form>"#))
}
```

- Shortcuts `{bcrypt,hmac,aes_gcm,ccp}_{cookie,session}_csrf(..)`; the keyed ones take `[u8; 32]`. Or
  `Csrf::new(cipher, store, finder)` to configure the store (`CookieStore::new().secure_cookie_policy(..)`).
- Finders: `FormFinder`, `HeaderFinder`, `JsonFinder` (no query finder).
- Only POST, PUT, PATCH and DELETE are checked. Render a fresh token on every page.
- Session-backed variants need `SessionHandler` hooped before them. With CORS, list the token header in
  `allow_headers`.

## Rate limiting (`rate-limiter`)

```rust
use salvo::rate_limiter::{BasicQuota, CelledQuota, FixedGuard, MokaStore, RateLimiter, RemoteIpIssuer, SlidingGuard};

let limiter = RateLimiter::new(FixedGuard::new(), MokaStore::new(), RemoteIpIssuer, BasicQuota::per_minute(100))
    .add_headers(true)                                        // x-ratelimit-limit, -remaining, -reset
    .skipper(|req: &mut Request, _: &Depot| req.uri().path() == "/health");
Router::with_path("login").hoop(limiter).post(login);
```

- Over the limit: 429. Verified over HTTP with the headers above.
- `FixedGuard` takes a `BasicQuota` (`per_second`, `per_minute`, `per_hour`, `set_seconds(n, secs)`, ...);
  `SlidingGuard` takes a `CelledQuota` (`CelledQuota::per_minute(60, 6)`: 60 per minute in 6 cells).
- Issuers: `RemoteIpIssuer` (peer address), `TrustedProxyIssuer::new([proxy_ips])` (reads `X-Forwarded-For` only
  from listed proxies), a closure `|req: &mut Request, depot: &Depot| -> Option<Key>`, or an `impl RateIssuer`
  (native `async fn issue`). Never use `ForwardedHeaderIssuer` (deprecated; trusts client headers).
- An issuer returning `None` answers **400**. Under `TestClient` there is no peer address, so `RemoteIpIssuer`
  answers 400 to every test request; use a closure issuer in tests.
- Per-key quotas: `impl QuotaGetter<Key>` (native `async fn get`, receives only the key). Any `Clone` quota is a
  `QuotaGetter` for itself.
- The store type is inferred when the limiter goes straight into `hoop(..)`; a standalone `let _ = RateLimiter::new(..)`
  needs its types annotated.

## Flash messages (`flash`)

```rust
use salvo::flash::{CookieStore, FlashDepotExt, FlashLevel};

let router = Router::new().hoop(CookieStore::new().into_handler()).push(..);

#[handler]
async fn save(depot: &mut Depot, res: &mut Response) {
    depot.outgoing_flash_mut().success("Saved");
    res.render(Redirect::other("/"));
}

#[handler]
async fn page(depot: &mut Depot) -> String {
    depot.incoming_flash()
        .map(|f| f.iter().map(|m| format!("[{}] {}", m.level.as_str(), m.value)).collect())
        .unwrap_or_default()
}
```

Levels `debug`, `info`, `success`, `warning`, `error` (chainable setters; `FlashLevel` enum with `as_str()` and
`Display`). `CookieStore` options: `name`, `max_age` (a `salvo::http::cookie::time::Duration`), `same_site`,
`secure`, `secure_cookie_policy`, `http_only`, `path`. `SessionStore::new().into_handler()` keeps messages in the
session (hoop `SessionHandler` first). `FlashHandler::new(store)` plus `minimum_level(FlashLevel::Warning)` drops
lower levels.

## Security headers and HTTPS

Salvo has no helmet-style middleware; set headers in a small hoop:

```rust
#[handler]
async fn security_headers(res: &mut Response) {
    let h = res.headers_mut();
    h.insert("strict-transport-security", "max-age=31536000; includeSubDomains".parse().unwrap());
    h.insert("x-content-type-options", "nosniff".parse().unwrap());
    h.insert("x-frame-options", "DENY".parse().unwrap());
    h.insert("referrer-policy", "strict-origin-when-cross-origin".parse().unwrap());
}
```

`ForceHttps::new().https_port(443)` (feature `force-https`, prelude) on the `Service` redirects plain HTTP; see
server.md for joining a port-80 listener.