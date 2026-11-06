# WebSockets, streaming and files

From the actix.rs pages WebSockets and Static Files, and the actix-ws 0.4 and actix-files 0.7 docs. Every snippet
here was compiled and run against actix-web 4.18.1, actix-ws 0.4.0 and actix-files 0.7.3.

## WebSockets with actix-ws

`actix-ws` turns a request into a session and a message stream; no actors. The old `actix-web-actors` crate
(`ws::start` with an `Actor`) is deprecated.

```toml
actix-ws = "0.4"
futures-util = { version = "0.3", default-features = false, features = ["std"] }
```

```rust
use actix_web::{rt, web, Error, HttpRequest, HttpResponse};
use actix_ws::AggregatedMessage;
use futures_util::StreamExt as _;

async fn echo(req: HttpRequest, body: web::Payload) -> Result<HttpResponse, Error> {
    let (response, mut session, stream) = actix_ws::handle(&req, body)?; // 400 if not a WebSocket upgrade
    let mut stream = stream.aggregate_continuations().max_continuation_size(1 << 20);

    rt::spawn(async move {
        while let Some(message) = stream.next().await {
            match message {
                Ok(AggregatedMessage::Text(text)) => {
                    if session.text(text).await.is_err() { break; } // the client went away
                }
                Ok(AggregatedMessage::Ping(bytes)) => { let _ = session.pong(&bytes).await; }
                Ok(AggregatedMessage::Close(reason)) => { let _ = session.close(reason).await; break; }
                _ => {}
            }
        }
    });
    Ok(response) // 101 Switching Protocols; the task keeps the connection
}

App::new().route("/ws", web::get().to(echo))
```

- The handler must return the response at once; the conversation happens in the spawned task (`rt::spawn` is
  `spawn_local`, so the task need not be `Send`).
- `Session` is `Clone`: keep clones in shared state to broadcast (a `Mutex<Vec<Session>>` or a
  `tokio::sync::broadcast` channel per room). Remove a session when a send returns `Err(Closed)`.
- Heartbeats: send `session.ping(b"")` on an interval and close the session when no pong arrived in time.
- CORS does not apply to WebSockets; check the `Origin` header yourself if it matters.

## Server-sent events and streaming responses

Any stream of `Result<Bytes, E>` (with `E: Into<Box<dyn std::error::Error>>`) can be a body:

```rust
async fn events() -> HttpResponse {
    let ticks = tokio_stream::wrappers::IntervalStream::new(tokio::time::interval(Duration::from_secs(1)))
        .enumerate()
        .map(|(n, _)| Ok::<_, std::convert::Infallible>(web::Bytes::from(format!("data: tick {n}\n\n"))));
    HttpResponse::Ok()
        .content_type("text/event-stream")
        .insert_header(("cache-control", "no-cache"))
        .streaming(ticks)
}
```

The stream is dropped when the client disconnects. `actix-web-lab`'s `sse` module adds typed events and keep-alive
comments if needed.

## Streaming request bodies

```rust
async fn upload(mut body: web::Payload) -> Result<HttpResponse, Error> {
    let mut size = 0usize;
    while let Some(chunk) = body.next().await {
        size += chunk?.len();
        if size > 10 * 1024 * 1024 {
            return Ok(HttpResponse::PayloadTooLarge().finish());
        }
        // write the chunk somewhere: tokio::fs, object storage, ...
    }
    Ok(HttpResponse::Ok().body(size.to_string()))
}
```

`web::Payload` has no size limit of its own. For multipart forms use `actix-multipart` (`Multipart` extractor, or
`MultipartForm` with `#[derive(MultipartForm)]` and `TempFile` fields, limits through `MultipartFormConfig`).

## Static files with actix-files

```toml
actix-files = "0.7"
```

```rust
use actix_files::{Files, NamedFile};

App::new()
    .service(Files::new("/static", "./static").index_file("index.html").prefer_utf8(true))
    // a single-page app: unknown paths get index.html
    .service(Files::new("/", "./dist").index_file("index.html").default_handler(
        actix_web::dev::fn_service(|req: actix_web::dev::ServiceRequest| async {
            let (req, _) = req.into_parts();
            let file = NamedFile::open("./dist/index.html")?;
            Ok(actix_web::dev::ServiceResponse::new(req.clone(), file.into_response(&req)))
        }),
    ))

async fn download() -> actix_web::Result<NamedFile> {
    Ok(NamedFile::open("report.pdf")?.use_last_modified(true)) // ETag, Last-Modified, Range, content type by extension
}
```

- The single-page fallback above was run: an unknown path gets `index.html` with 200.
- `Files` must be registered with `App::service` (not `route`) to serve sub-paths. It refuses `..` traversal
  (`/static/../Cargo.toml` is 404) and hidden files by default; `show_files_listing()` enables directory listings,
  `use_hidden_files()` dot files.
- Register `Files::new("/", ..)` last: it matches every path.
- `NamedFile::open` is synchronous (`std::fs`); there is no `open_async` in actix-files 0.7.3, whatever older
  examples show. For many large files on a busy worker, open them in `web::block`.
- `NamedFile` options: `set_content_type`, `set_content_disposition`, `use_etag`, `use_last_modified`,
  `disable_content_disposition`. The same options exist on `Files` (`use_etag`, `use_last_modified`, `mime_override`).
