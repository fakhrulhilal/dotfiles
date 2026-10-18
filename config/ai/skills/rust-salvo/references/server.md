# Server, TLS and proxy

## Server and listeners

```rust
let acceptor = TcpListener::new("0.0.0.0:8080").bind().await;      // panics if the address is taken
let acceptor = TcpListener::new(addr).try_bind().await?;            // Result instead (template main.rs)
let server = Server::new(acceptor).max_connections(10_000);         // back-pressure above that
server.serve(service_or_router).await;
```

- `serve` takes a `Router` or a `Service` (anything `Into<Service>`). Build the `Service` yourself for service hoops
  and a catcher.
- Listeners: `TcpListener::new(addr)` (`"host:port"` or `(ip, port)`), `UnixListener` (feature `unix`),
  `.rustls(config)` / `.native_tls(..)` / `.openssl(..)`, `QuinnListener` (HTTP/3), `.acme()`. Combine any two with
  `.join(other)` before `bind()`.
- Salvo logs each bound address (`listening [HTTP/1.1] on http://...`). Plain TCP serves HTTP/1.1; h2c needs
  `http2-cleartext`, TLS listeners negotiate HTTP/2.

## Graceful shutdown

`Server::handle()` (default feature `server-handle`) returns a cloneable `ServerHandle`: `stop_graceful(timeout)`
stops accepting and waits for in-flight requests (`None` waits forever, `Some(d)` or a bare `Duration` forces stop
after `d`); `stop_forceful()` stops at once. Both return immediately.

```rust
let server = Server::new(acceptor);
tokio::spawn(shutdown_on_signal(server.handle()));
server.serve(service).await;

async fn shutdown_on_signal(handle: ServerHandle) {
    let ctrl_c = async { tokio::signal::ctrl_c().await.expect("install Ctrl+C handler") };
    #[cfg(unix)]
    let terminate = async {
        use tokio::signal::unix::{SignalKind, signal};
        signal(SignalKind::terminate()).expect("install SIGTERM handler").recv().await;
    };
    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();
    tokio::select! { _ = ctrl_c => {}, _ = terminate => {} }
    handle.stop_graceful(std::time::Duration::from_secs(25));      // below Kubernetes' 30 s grace period
}
```

Needs tokio's `signal` feature. Without it, SIGTERM (Docker, Kubernetes, systemd) kills in-flight requests. Close
pools and flush buffers before calling `stop_graceful`, or after `serve` returns. To drain behind a load balancer,
flip a flag that makes the readiness probe answer 503, wait a few seconds, then stop.

## TLS with rustls (`rustls`)

```rust
use salvo::conn::rustls::{Keycert, RustlsConfig};

let config = RustlsConfig::new(
    Keycert::new().cert_from_path("certs/cert.pem")?.key_from_path("certs/key.pem")?,
);
// or from memory: Keycert::new().cert(cert_pem.as_slice()).key(key_pem.as_slice())
let acceptor = TcpListener::new("0.0.0.0:443").rustls(config).bind().await;
```

HTTP to HTTPS (feature `force-https`): serve both ports and hoop `ForceHttps` on the service.

```rust
let service = Service::new(router).hoop(ForceHttps::new().https_port(443));
let acceptor = TcpListener::new("0.0.0.0:443").rustls(config)
    .join(TcpListener::new("0.0.0.0:80"))
    .bind()
    .await;
Server::new(acceptor).serve(service).await;
```

In containers TLS usually ends at the ingress or proxy; then serve plain HTTP and set cookie policies to `Always`
(security.md).

## ACME / Let's Encrypt (`acme`)

`.acme()` is a method on `TcpListener`; there is no `AcmeListener::builder()`. Types live in `salvo::acme`
(not `salvo::conn::acme`).

```rust
use salvo::acme::LETS_ENCRYPT_STAGING;

let mut router = Router::new().get(hello);
let listener = TcpListener::new("0.0.0.0:443")
    .acme()
    .directory("letsencrypt-staging", LETS_ENCRYPT_STAGING)  // drop for production (the default directory)
    .cache_path("/var/lib/app/acme")                        // persist, or you hit rate limits on restarts
    .add_domain("example.com")
    .add_contact("mailto:admin@example.com")
    .http01_challenge(&mut router);                         // mounts the challenge route; needs port 80
let acceptor = listener.join(TcpListener::new("0.0.0.0:80")).bind().await;
Server::new(acceptor).serve(router).await;
```

Without `http01_challenge` the default is TLS-ALPN-01 on 443 only. `dns01_challenge(Arc<dyn Solver>)` for
wildcards. `domains(vec![..])`, `contacts(vec![..])`, `key_type(KeyType::EcdsaP256)` (the default). The method is
`directory(name, url)`, not `directory_url`, and there is no `challenge_type(..)`.

## HTTP/3 (`quinn`)

```rust
let config = RustlsConfig::new(Keycert::new().cert(cert.as_slice()).key(key.as_slice()));
let tcp = TcpListener::new(("0.0.0.0", 443)).rustls(config.clone());
let acceptor = QuinnListener::new(config.build_quinn_config()?, ("0.0.0.0", 443)).join(tcp).bind().await;
```

## Reverse proxy (`proxy`)

`Proxy` is a handler; mount it with `goal` under a wildcard.

```rust
use salvo::proxy::{HyperClient, Proxy};

let users = Proxy::new(vec!["http://users:3001", "http://users-2:3001"], HyperClient::default())  // round robin
    .client_ip_forwarding(true);                        // adds the caller to X-Forwarded-For
let router = Router::with_path("api")
    .hoop(auth)                                         // runs before proxying; skip_rest() blocks it
    .push(Router::with_path("users/{**rest}").goal(users));
```

- Upstreams: `&str`, `String`, arrays or `Vec`s of them (rotated round robin), or a custom `Upstreams` impl.
  `Proxy::use_hyper_client(upstreams)` is the short form. The `proxy` feature also includes `ReqwestClient` and
  the Unix socket client.
- The forwarded path is the `{**rest}` part. Rewrite with `.url_path_getter(|req, depot| Option<String>)` and
  `.url_query_getter(..)`. The `Host` header includes a non-default upstream port; `.host_header_getter(
  default_host_header_getter)` sends the bare host.
- Defaults: `strict_path_normalization(true)` rejects ambiguous encoded traversal; the `Authorization` header is
  forwarded unless `.strip_authorization_header(true)`.
- WebSocket upgrades pass through.