# OpenAPI (`oapi`)

`salvo-oapi` generates an OpenAPI 3.1 document from the router at runtime. With the `oapi` feature the prelude
exports `endpoint`, `ToSchema`, `ToParameters`, `OpenApi` and `SwaggerUi`; extractors live in
`salvo::oapi::extract`. The macro attribute reference is `salvo-oapi`'s `docs/endpoint.md` (also on docs.rs under
`salvo::oapi::endpoint`). The template documents every route this way, including problem-details errors.

## `#[endpoint]`

`#[endpoint]` is `#[handler]` plus an operation. It documents only what it can see in the signature:

- parameters and bodies from extractors (`PathParam<T>`, `QueryParam<T, R>`, `HeaderParam`, `CookieParam`,
  `JsonBody<T>`, `FormBody<T>`, `ToParameters` structs). `req.param()`/`req.query()` calls are invisible.
- responses from the return type, through `EndpointOutRegister`: `Json<T: ToSchema>` (200), `StatusCode` (every
  1xx to 3xx plus the `StatusError` set), `StatusError`, `Problem<E>`, `String`/`&str`, and `Result<T, E>` (both
  sides).
- the summary and description from the doc comment (first line is the summary).

```rust
use salvo::oapi::extract::{JsonBody, PathParam, QueryParam};
use salvo::prelude::*;

/// Get one todo.
#[endpoint(tags("todos"))]
async fn show(id: PathParam<u64>, depot: &mut Depot) -> Result<Json<Todo>, ApiError> { .. }

/// Search todos.
#[endpoint(tags("todos"))]
async fn search(q: QueryParam<String, false>, paging: Paging) -> Json<Vec<Todo>> { .. }
```

Every type in the signature must implement the matching trait (`ToSchema` for bodies and `Json` payloads,
`ToParameters` for parameter structs, `EndpointOutRegister` for the return type), or the endpoint does not
compile.

## Attributes

`#[endpoint(...)]` takes `operation_id = ..`, `tags(..)`, `summary = ..`, `description = ..`, `request_body = ..` or
`request_body(..)`, `status_codes(..)`, `responses(..)`, `parameters(..)`, `security(..)`, `deprecated`.

```rust
#[endpoint(
    tags("todos"),
    status_codes(201, 400, 422),                  // keep only these from the generated responses
    responses(
        (status_code = 201, description = "Created", body = Todo),
    ),
)]
async fn create(body: JsonBody<NewTodo>, res: &mut Response) -> Result<Json<Todo>, ApiError> { .. }
```

`status_codes` filters what the return type registered; `responses` adds or overrides entries. A handler that sets
201 with `res.status_code(..)` still registers `Json<T>` as 200, so list `201` in `responses` when the document must
say 201.

Request bodies the extractors cannot describe (raw multipart, `req.payload()`):

```rust
#[derive(ToSchema)]
struct UploadForm {
    /// The file.
    #[salvo(schema(value_type = String, format = Binary))]
    file: String,
}

#[endpoint(tags("files"), request_body(content = UploadForm, content_type = "multipart/form-data"))]
async fn upload(req: &mut Request) -> Result<Json<Uploaded>, StatusError> { .. }
```

`request_body(content = "multipart/form-data")` (a string as the content) does not parse; `content` is a type.

## Schemas

```rust
use salvo::oapi::{ToParameters, ToSchema};
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, ToSchema)]
#[salvo(schema(example = json!({ "id": 1, "title": "write docs", "done": false })))]
struct Todo {
    id: u64,
    /// What to do.
    #[salvo(schema(pattern = "^.+$", max_length = 200))]
    title: String,
    #[salvo(schema(format = "email"))]
    owner: Option<String>,
    done: bool,
}

#[derive(Deserialize, ToParameters)]
struct Paging {
    /// Page number.
    #[salvo(parameter(default = 1, minimum = 1))]
    page: Option<u32>,
    #[salvo(parameter(default = 20, minimum = 1, maximum = 100))]
    per_page: Option<u32>,
}
```

Field doc comments become descriptions. `ToParameters` fields are query parameters unless marked otherwise; path
parameters are always `required: true`. Generic and renamed types: see `docs/derive_to_schema.md`.

Component names default to the full Rust path (`my_crate.todos.Todo`). For plain names set the global namer once,
before the document is built (template `openapi()`):

```rust
use salvo::oapi::naming::{self, FlexNamer};

naming::set_namer(FlexNamer::new().short_mode(true).generic_delimiter("_", ""));   // Todo, Problem_Fields
```

Or name one type with `#[salvo(schema(name = "Todo"))]`. Two types with the same short name get a numeric suffix.

## The document and UIs

```rust
use salvo::oapi::{Info, License, OpenApi};

let router = app_router();
let doc = OpenApi::new("Todo API", "0.1.0")
    .info(Info::new("Todo API", "0.1.0").description("..").license(License::new("MIT")))
    .merge_router(&router);
let router = router
    .unshift(doc.into_router("/api/openapi.json"))
    .unshift(SwaggerUi::new("/api/openapi.json").into_router("/swagger-ui"));
```

- Merge first, then `unshift` the doc routes, so the document does not describe itself.
- `OpenApi` has `info`, `servers`, `security`, `add_security_scheme`, `tags`, `merge_router`,
  `openapi_version`. Description, contact and license go on `Info` (`Contact::new().name(..).email(..)`).
- Routes without a method filter (`goal(..)`) are skipped. `.openapi_version(OpenApiVersion::Version3_2)` emits 3.2
  and then also documents `QUERY` and custom methods; keep 3.1 for older clients.
- Other UIs: `salvo::oapi::scalar::Scalar`, `rapidoc::RapiDoc`, `redoc::ReDoc`, each
  `::new(spec_url).into_router(path)`.
- `Parameter::location(..)` replaces the deprecated `parameter_in(..)` when building parameters by hand.

## Security schemes

```rust
use salvo::oapi::security::{Http, HttpAuthScheme, SecurityScheme};

let doc = OpenApi::new("API", "1.0.0")
    .add_security_scheme("bearer", SecurityScheme::Http(Http::new(HttpAuthScheme::Bearer).bearer_format("JWT")))
    .merge_router(&router);

#[endpoint(security(["bearer" = []]))]          // ("bearer" = []) also accepted
async fn profile() -> &'static str { "me" }
```

The scheme only documents; the `JwtAuth` hoop enforces (security.md).

## Documenting a custom error

A custom error type needs `EndpointOutRegister`. Register one response per status it can produce, with the schema
of its body:

```rust
use salvo::oapi::{self, Components, EndpointOutRegister, Operation, ToSchema};

impl EndpointOutRegister for ApiError {
    fn register(components: &mut Components, operation: &mut Operation) {
        let problem = PlainProblem::to_schema(components);              // feature rfc9457
        operation.responses.insert(
            "404",
            oapi::Response::new("Not found").add_content("application/problem+json", problem),
        );
    }
}
```

`Writer` for the same type needs `#[async_trait]` (errors.md). With `rfc9457`, returning `Problem<E>` directly
needs no impl; it registers a `default` response with the `Problem` schema.