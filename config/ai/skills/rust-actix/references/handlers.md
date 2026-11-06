# Handlers, extractors and responders

From the actix.rs pages Handlers, Extractors, Requests and Responses, and the `FromRequest`, `Responder` and
`HttpResponseBuilder` docs. Behaviour marked *(run)* was checked against actix-web 4.18.1.

## Handlers

A handler is an `async fn` (or closure returning a future) with up to 16 arguments, each `FromRequest`, returning a
`Responder`. It must be `Clone + 'static`; captured values in a closure must be too.

```rust
async fn update(
    state: web::Data<AppState>,
    id: web::Path<Uuid>,
    changes: web::Json<UpdateTodo>, // body extractor last
) -> Result<web::Json<Todo>, ApiError> { /* ... */ }

web::resource("/todos/{id}").route(web::patch().to(update))
```

- Extractors run in argument order; the first failure becomes the response and the handler never runs. Put the
  body extractor last so path and query errors win.
- Only one argument can consume the body (`Json`, `Form`, `Bytes`, `String`, `Payload`). A second one sees an empty
  payload. For "JSON or raw bytes", use `Either<Json<T>, Bytes>`.
- Wrap an extractor in `Option<T>` (failure → `None`) or `Result<T, T::Error>` to handle its failure in the handler.
- Async handlers never block the worker only if they `.await` instead of blocking: see server.md.

## Extractors

| Extractor                    | Reads                                         | Config (`app_data`)      | Default failure *(run)* |
|------------------------------|-----------------------------------------------|--------------------------|-------------------------|
| `web::Path<T>`               | path parameters, tuple or `Deserialize` struct| `PathConfig`             | **404** text            |
| `web::Query<T>`              | query string (`serde_urlencoded`)             | `QueryConfig`            | 400 text                |
| `web::Json<T>`               | body, `application/json` or `+json`; 2 MiB    | `JsonConfig`             | 400 text (wrong content type too), 413 over the limit |
| `web::Form<T>`               | `application/x-www-form-urlencoded`; 16 KiB   | `FormConfig`             | 400 text                |
| `web::Bytes`, `String`       | the whole body; 256 KiB                       | `PayloadConfig`          | 413 / 400               |
| `web::Payload`               | the body as a stream of `Bytes`               | none                     |                         |
| `web::Data<T>`, `web::ThinData<T>` | app data by type                         | none                     | 500 text                |
| `web::Header<T>`             | a typed header (`header::ContentType`, ...)   | none                     | 400                     |
| `HttpRequest`                | the request head, app data, `match_info`, `url_for`, `conn_info` | none   |                         |
| `web::ReqData<T>`            | a value a middleware put in the request extensions | none                | 500 if missing          |
| `ConnectionInfo`, `PeerAddr` | scheme/host/real IP, socket address           | none                     |                         |
| `Method`, `Uri`, `Version`   | parts of the head                              | none                     |                         |

Configs are app data: `.app_data(JsonConfig::default().limit(64 * 1024).error_handler(handler))` on the `App`, a
scope or a resource; the nearest one wins. `JsonConfig::content_type(|mime| ..)` accepts other types,
`content_type_required(false)` accepts a missing `Content-Type`.

Error handlers have the signature `fn(ExtractorError, &HttpRequest) -> actix_web::Error`; convert into your own
`ResponseError` type (`MyError::from(..).into()`). See errors.md for JSON problem details.

`web::Json<T>`, `Path<T>`, `Query<T>`, `Form<T>`, `Data<T>` deref to `T`; `.into_inner()` moves it out.

## A custom extractor

```rust
pub struct ApiKey(pub String);

impl FromRequest for ApiKey {
    type Error = ApiError;
    type Future = std::future::Ready<Result<Self, Self::Error>>;

    fn from_request(req: &HttpRequest, _: &mut dev::Payload) -> Self::Future {
        let key = req.headers().get("x-api-key").and_then(|v| v.to_str().ok());
        std::future::ready(match key {
            Some(key) => Ok(ApiKey(key.to_owned())),
            None => Err(ApiError::Unauthorized),
        })
    }
}
```

An async extractor sets `type Future = LocalBoxFuture<'static, ..>` (from `futures_util`) or another concrete future
type; it must not borrow `req` (clone what it needs).

## Responders

`Responder` is implemented for `HttpResponse`, `&'static str`, `String`, `Bytes`, `Json<T: Serialize>`,
`(R, StatusCode)`, `Option<R>` (None = 404), `Result<R, E: Into<Error>>`, `Either<L, R>`, `web::Redirect`,
`actix_files::NamedFile`, and `CustomizeResponder`.

```rust
HttpResponse::Created().insert_header((header::LOCATION, url)).json(&todo) // builder; .json/.body/.finish end it
HttpResponse::NoContent().finish()
HttpResponse::build(StatusCode::IM_A_TEAPOT).content_type(ContentType::plaintext()).body("tea")
web::Json(todo).customize().with_status(StatusCode::CREATED).insert_header(("x-id", id))
web::Redirect::to("/new").permanent() // 307 by default; .permanent() = 308, .see_other() = 303
```

A builder's `.body()`, `.json()` or `.finish()` may be called once; a second call panics.

Implement `Responder` for a type:

```rust
impl Responder for Todo {
    type Body = BoxBody;
    fn respond_to(self, _req: &HttpRequest) -> HttpResponse<Self::Body> {
        HttpResponse::Ok().json(self)
    }
}
```

`impl Responder` as a return type works when every branch has the same type; use `Either` or `HttpResponse` when
branches differ.

## Bodies

- Streaming response: `HttpResponse::Ok().streaming(stream)` where the stream yields `Result<Bytes, E: Into<Box<dyn
  Error>>>`.
- Streaming request: take `web::Payload` and `while let Some(chunk) = payload.next().await` (needs
  `futures_util::StreamExt`); enforce a size limit yourself.
- actix decodes chunked transfer encoding; compressed request bodies are decoded only with a `compress-*` feature.
- Response compression is the `Compress` middleware (middleware.md); a response with its own `Content-Encoding`
  header is left alone.
