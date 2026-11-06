# Errors

From the actix.rs Errors page, the `ResponseError`, `Error` and `error` module docs, and the template's
`src/problem.rs`. Behaviour marked *(run)* was checked against actix-web 4.18.1.

## The model

- `actix_web::Error` is a boxed `ResponseError`. A handler returns `Result<R, E>` with `E: Into<actix_web::Error>`,
  which every `ResponseError` is.
- `ResponseError` has two methods with defaults: `status_code()` (500) and `error_response()` (the status and the
  `Display` text as `text/plain`). Override `error_response()` to choose the body.
- A handler's error becomes a response **inside** the route: middleware sees an ordinary `Ok(ServiceResponse)`
  whose `response().error()` is `Some(&Error)`. Only a middleware's own `Err` travels out as `Err`.
- `error_response()` gets `&self` only, no request. Anything that needs the request (`instance`, the matched route)
  is added by a middleware afterwards.

```rust
#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    #[error("{0}")]
    NotFound(String),
    #[error(transparent)]
    Database(#[from] sqlx::Error),
}

impl ResponseError for ApiError {
    fn status_code(&self) -> StatusCode {
        match self {
            Self::NotFound(_) => StatusCode::NOT_FOUND,
            Self::Database(_) => StatusCode::INTERNAL_SERVER_ERROR,
        }
    }

    fn error_response(&self) -> HttpResponse {
        let detail = match self {
            Self::NotFound(detail) => detail.clone(),
            Self::Database(error) => {
                tracing::error!(%error, "query failed"); // the cause stays in the log
                "The request could not be completed.".to_owned()
            }
        };
        HttpResponse::build(self.status_code()).json(serde_json::json!({ "detail": detail }))
    }
}
```

## Helpers, and what they leak

`error::ErrorBadRequest(e)`, `ErrorNotFound(e)`, `ErrorInternalServerError(e)`, ... wrap any `Debug + Display` value
into an `Error` whose body is `e.to_string()`. `error::InternalError::from_response(e, response)` keeps `e` for
logging and sends `response` instead. Use the helpers for messages meant for the client only; for internal causes
log them and return a fixed message (or your own `ResponseError`).

Foreign `ResponseError` impls include `std::io::Error` (404 for `NotFound`, 403 for `PermissionDenied`, else 500),
`serde_json::Error` and `Box<dyn std::error::Error>` (500, with their text in the body), `serde::de::value::Error`
and `Utf8Error` (400), and the extractor errors below. `?` on any of them in a handler returning
`actix_web::Result` therefore sends the error's text to the client.

## Extractor failures

Without configuration they are `text/plain` responses with the error's text *(run)*:

| Extractor error                        | Default status | Text example                                             |
|----------------------------------------|----------------|----------------------------------------------------------|
| `JsonPayloadError::ContentType`        | 400            | `Content type error`                                     |
| `JsonPayloadError::Deserialize`        | 400            | `Json deserialize error: missing field `title` at line 1 column 7` |
| `JsonPayloadError::Overflow*`          | 413            |                                                          |
| `PathError::Deserialize`               | **404**        | `can not parse "abc" to a u32`                           |
| `QueryPayloadError::Deserialize`       | 400            | `Query deserialize error: invalid digit found in string` |
| `UrlencodedError` (Form)               | 400 / 413      |                                                          |
| missing `Data<T>`                      | 500            | `Requested application data is not configured correctly. ...` |

Change them with the config's `error_handler`. The error enums are `#[non_exhaustive]`: end every `match` with a
catch-all arm.

```rust
pub fn json_error(error: JsonPayloadError, _req: &HttpRequest) -> actix_web::Error {
    match error {
        JsonPayloadError::OverflowKnownLength { .. } | JsonPayloadError::Overflow { .. } => {
            ApiError::Rejected(StatusCode::PAYLOAD_TOO_LARGE, error.to_string()).into()
        }
        JsonPayloadError::ContentType => ApiError::Rejected(StatusCode::UNSUPPORTED_MEDIA_TYPE, "Expected JSON".into()).into(),
        JsonPayloadError::Deserialize(e) if e.is_data() => ApiError::Rejected(StatusCode::UNPROCESSABLE_ENTITY, e.to_string()).into(),
        other => ApiError::Rejected(StatusCode::BAD_REQUEST, other.to_string()).into(), // syntax, EOF, payload errors
    }
}

App::new()
    .app_data(JsonConfig::default().error_handler(json_error))
    .app_data(PathConfig::default().error_handler(|e, _| ApiError::Rejected(StatusCode::BAD_REQUEST, e.to_string()).into()))
    .app_data(QueryConfig::default().error_handler(|e, _| ApiError::Rejected(StatusCode::BAD_REQUEST, e.to_string()).into()));
```

`serde_json::Error::is_data()` separates "valid JSON of the wrong shape" (422 in most APIs) from syntax and EOF
errors (400).

## RFC 9457 problem details everywhere

Four sources of error responses have to be covered for a JSON API whose every error is
`application/problem+json`:

1. Handler errors: `ApiError::error_response()` renders the problem.
2. Extractor errors: the config error handlers above convert to `ApiError`.
3. Router errors: the app's `default_service` (404) and each resource's built-in 405 (empty body, `Allow` header),
   or a `default_service` per resource that renders the 405 problem with your own `Allow` format.
4. Middleware errors, such as actix-cors refusing a preflight: it answers with `req.error_response(CorsError)`, a
   text response with the error attached.

The template's `problem_details` middleware (`from_fn`) finishes all of them: it takes the `Problem` that
`into_response()` stored in the response extensions (`HttpResponse::extensions_mut()`), or builds one for an error
response that is not a problem yet, sets `instance` to `request.path()` (taken from the `ServiceResponse`, not from
a clone kept across `next.call`, see pitfalls.md), and copies the original headers except `Content-Type` and
`Content-Length`, so `Allow` and the CORS headers survive. Register it inside the logger and outside CORS:

```rust
App::new()
    // ...
    .wrap(cors)                                   // innermost of the three
    .wrap(from_fn(problem::problem_details))
    .wrap(from_fn(log_request))                   // outermost: sees the finished response
```

## `ErrorHandlers`

`middleware::ErrorHandlers::new().handler(StatusCode::NOT_FOUND, f)` (or `.default_handler(f)`,
`.default_handler_client(f)`, `.default_handler_server(f)`) rewrites responses by status. `f(ServiceResponse<B>) ->
Result<ErrorHandlerResponse<B>>`; return `ErrorHandlerResponse::Response(res.map_into_left_body())` after changing
headers, or build a new body with `res.into_parts()` and `map_into_right_body()`. Simpler than `from_fn` when only a
few statuses need a body, but it cannot see whether a response already has one without inspecting it.

## Panics

A panic in a handler is not caught *(run)*: the connection is dropped without a response (the client sees a reset
or an empty reply), the panic is printed, and the server keeps serving other requests. There is no built-in
catch-panic middleware; return errors instead of panicking, and treat a panic as a bug.

## Logging errors

actix logs through the `log` crate. With `tracing-subscriber`'s default `tracing-log` feature, `try_init()` forwards
those records. `middleware::Logger` writes access lines through `log`; set `RUST_LOG=actix_web=debug` to see
error details it logs at debug level.
