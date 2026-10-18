# Handlers, responses and extraction

## Handler signatures

`#[handler]` turns an `async fn` (or `fn`) into a type that implements `Handler`. Parameters are optional and in any
order: `req: &mut Request`, `depot: &mut Depot`, `res: &mut Response`, `ctrl: &mut FlowCtrl`, plus any number of
extractors (types implementing `Extractible`).

The function becomes a unit struct with the same name. Nothing else in the module, and no closure parameter inside
it, may use that name.

```rust
use salvo::prelude::*;

#[handler]
async fn show(req: &mut Request, depot: &mut Depot, res: &mut Response) {
    res.render(format!("{} {}", req.method(), req.uri().path()));
}
```

`#[endpoint]` (feature `oapi`) does the same and also generates the OpenAPI operation; see openapi.md. A struct can
implement `Handler` directly with `#[async_trait] impl Handler for X { async fn handle(&self, req, depot, res, ctrl) }`.

## Responses

A handler returns anything that implements `Writer`. Every `Scribe` is a `Writer`; built-ins:

| Return type                                                                         | Response                                   |
|-------------------------------------------------------------------------------------|--------------------------------------------|
| `()`                                                                                | 200 with an empty body                     |
| `&'static str`, `String`                                                            | `text/plain; charset=utf-8`                |
| `Json<T: Serialize>`                                                                | `application/json`                         |
| `Text::Html(..)`, `Text::Plain`, `Text::Json`, `Text::Xml`, `Text::Css`, `Text::Js` | that content type (`salvo::writing::Text`) |
| `StatusCode`                                                                        | that status, empty body                    |
| `Redirect::found(..)` and friends                                                   | 3xx with `Location` (routing.md)           |
| `StatusError`                                                                       | that status; body rendered by the catcher  |
| `Problem` (feature `rfc9457`)                                                       | `application/problem+json` (errors.md)     |
| `Result<T, E>`                                                                      | `T` or `E`, both `Writer`                  |

There is no `salvo::writing::Html`; use `Text::Html`.

Writing to `res` directly:

```rust
#[handler]
async fn created(res: &mut Response) {
    res.status_code(StatusCode::CREATED);                       // setter
    res.headers_mut().insert("x-custom", "value".parse().unwrap());
    res.render(Json(serde_json::json!({ "ok": true })));        // render any Scribe
}
```

Read the status with the field `res.status_code` (`Option<StatusCode>`; `None` until something sets it). Set it
before rendering when returning `Json` with a non-200 status (template `todos::create`). `res.write_body(bytes)`
appends raw bytes; `res.body(..)` replaces the body.

## Reading the request

```rust
let id: Option<i64> = req.param("id");                  // path parameter
let page: Option<u32> = req.query("page");              // ?page=2
let auth: Option<String> = req.header("authorization");  // header, deserialized
let name: Option<String> = req.form("name").await;      // one urlencoded or multipart field
let body: NewTodo = req.parse_json().await?;            // whole JSON body
let form: Login = req.parse_form().await?;              // whole form body
let paging: Paging = req.parse_queries()?;              // all query parameters as a struct
let raw: &bytes::Bytes = req.payload().await?;          // raw body, honours the max size
let ip = req.remote_addr();                             // peer address (proxy aware only through issuers)
```

`param`, `query` and `header` return `Option`; `try_param`, `try_query` and `try_header` return a `Result` with the
parse error. `parse_*` return `ParseResult<T>`.

## Extractors

Extractors make the input part of the signature, which is what OpenAPI documents. They live in
`salvo::oapi::extract` (feature `oapi`; most are also in `salvo::extract`).

| Extractor                        | Source                                          | Notes                                        |
|----------------------------------|-------------------------------------------------|----------------------------------------------|
| `PathParam<T>`                   | path parameter of the same name as the argument | always required; `*id` or `id.into_inner()`  |
| `QueryParam<T, REQUIRED = true>` | query parameter                                 | `QueryParam<T, false>` derefs to `Option<T>` |
| `HeaderParam<T, REQUIRED>`       | header                                          |                                              |
| `CookieParam<T, REQUIRED>`       | cookie                                          |                                              |
| `JsonBody<T>`                    | JSON body                                       | needs a JSON content type; `into_inner()`    |
| `FormBody<T>`                    | urlencoded or multipart body                    |                                              |
| a struct deriving `ToParameters` | several params at once                          | openapi.md                                   |
| a struct deriving `Extractible`  | any mix of sources                              | below                                        |

```rust
use salvo::oapi::extract::{JsonBody, PathParam, QueryParam};

#[endpoint]
async fn search(id: PathParam<u64>, q: QueryParam<String, false>, body: JsonBody<Filter>) -> String {
    format!("{} {:?} {:?}", *id, q.as_deref(), body.into_inner())
}
```

An extractor that fails answers **400** with detail `parse http data failed` before the handler runs: malformed
JSON, a JSON body sent as `text/plain` or a form, a missing required parameter, `PathParam<u64>` given `abc`. The
handler never sees these, so validate semantics (lengths, ranges) inside it and answer 422 yourself.

## `Extractible` structs

```rust
use salvo::prelude::*;
use serde::Deserialize;

#[derive(Extractible, Deserialize, Debug)]
#[salvo(extract(default_source(from = "body")))]              // JSON body by default
struct CreateUser {
    name: String,
    email: String,
}

#[derive(Extractible, Deserialize)]
#[salvo(extract(default_source(from = "body"), default_format = "form"))]
struct LoginForm {
    username: String,
    password: String,
}

#[derive(Extractible, Deserialize)]
struct UpdateUser {
    #[salvo(extract(source(from = "param")))]
    id: i64,
    #[salvo(extract(source(from = "body")))]
    name: String,
}

#[handler]
async fn create_user(user: CreateUser) -> StatusCode {
    StatusCode::CREATED
}
```

Sources: `body`, `query`, `param`, `header`, `cookie`, `depot`. A `depot` source reads values that middleware
stored with `depot.insert("user_id", 7i64)`; it supports strings, integers, floats and `bool`. For other types read
the depot by hand.

## Validation

Salvo has no validation layer. Validate after extraction and return an error that renders 422, either by hand (the
template's `validate_title` and `ApiError::Validation`) or with the `validator` crate:

```rust
use validator::Validate;

#[derive(Extractible, Deserialize, Validate)]
#[salvo(extract(default_source(from = "body")))]
struct CreateUser {
    #[validate(length(min = 1, max = 100))]
    name: String,
    #[validate(email)]
    email: String,
}

#[handler]
async fn create_user(user: CreateUser) -> Result<StatusCode, StatusError> {
    user.validate().map_err(|e| StatusError::unprocessable_entity().brief(e.to_string()))?;
    Ok(StatusCode::CREATED)
}
```

## Depot

Request-scoped storage shared by the middleware and handlers of one request.

| Method                                                                       | Returns                                           |
|------------------------------------------------------------------------------|---------------------------------------------------|
| `depot.insert(key, value)`                                                   | stores under a string key                         |
| `depot.get::<V>(key)`                                                        | `Result<&V, Option<&Box<dyn Any + Send + Sync>>>` |
| `depot.get_mut::<V>(key)`                                                    | same shape, `&mut V`                              |
| `depot.remove(key)`                                                          | `Option<Box<dyn Any + Send + Sync>>`              |
| `depot.insert_typed(value)`                                                  | stores by type, one value per type                |
| `depot.get_typed::<V>()`                                                     | `Result<&V, _>`                                   |
| `depot.get_typed_mut::<V>()`, `remove_typed::<V>()`, `contains_typed::<V>()` |                                                   |

`Result`, not `Option`: write `.ok()`, `.map_err(..)` or `.expect(..)`, not `.ok_or(..)`. `inject`, `obtain` and
`obtain_mut` are deprecated aliases of the typed methods.

Application-wide state (pools, caches, config) is injected into every request's depot with
`Router::new().hoop(affix_state::inject(value))` (feature `affix-state`; the value must be `Clone + Send + Sync`).
Wrap mutable state in `Arc<Mutex<..>>`/`Arc<RwLock<..>>` and keep the clone cheap. See the template's `Store` and
database.md.