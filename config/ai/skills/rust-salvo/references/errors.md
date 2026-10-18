# Errors

An error is a `Writer` that sets a 4xx/5xx status. If it leaves the body empty (or stores a `StatusError` as the
body), the service's `Catcher` renders it. The template uses both paths: `ApiError` writes its own problem
details, and `problem::catch` renders everything else.

## `StatusError`

```rust
#[handler]
async fn get_user(req: &mut Request) -> Result<Json<User>, StatusError> {
    let id = req.param::<i64>("id").ok_or_else(|| StatusError::bad_request().brief("missing id"))?;
    find_user(id).await.map(Json).ok_or_else(|| StatusError::not_found().brief("user not found"))
}
```

Constructors exist for every 4xx and 5xx status: `bad_request()`, `unauthorized()`, `forbidden()`, `not_found()`,
`method_not_allowed()`, `conflict()`, `unprocessable_entity()`, `too_many_requests()`, `internal_server_error()`,
`not_implemented()`, `bad_gateway()`, `service_unavailable()`, `gateway_timeout()`, ... and
`StatusError::from_code(code) -> Option<StatusError>`. There are none for 1xx to 3xx: set `StatusCode::NOT_MODIFIED`
with `res.status_code(..)`.

Builders: `.brief(..)` (short message), `.detail(..)` (longer text), `.cause(err)` (any error, exposed as
`source()`). The fields `code`, `name`, `brief`, `detail` are public.

What the client sees: the default catcher always shows `name` and `brief`. `detail` and `cause` are hidden unless
the `SALVO_STATUS_ERROR` environment variable says otherwise: `never_detail,never_cause` is the default,
`debug_detail,debug_cause` shows them in debug builds, `force_detail,force_cause` always. Problem catchers put
`brief` into the problem's `detail`. Never put internal
error text in `brief`; log it and return a fixed message:

```rust
let rows = query(pool).await.map_err(|e| {
    tracing::error!(error = %e, "query failed");
    StatusError::internal_server_error().brief("request failed")
})?;
```

## Custom error types

Implement `Writer` (with `#[async_trait]`) for full control. Under `#[endpoint]` also implement
`EndpointOutRegister` (openapi.md).

```rust
use salvo::prelude::*;

#[derive(Debug, thiserror::Error)]
enum ApiError {
    #[error("not found: {0}")]
    NotFound(String),
    #[error(transparent)]
    Database(#[from] sqlx::Error),
}

#[async_trait]
impl Writer for ApiError {
    async fn write(self, req: &mut Request, _depot: &mut Depot, res: &mut Response) {
        match self {
            Self::NotFound(what) => res.render(Problem::new(StatusCode::NOT_FOUND).detail(what)),
            Self::Database(e) => {
                tracing::error!(error = %e, path = req.uri().path(), "database error");
                res.render(StatusError::internal_server_error().brief("request failed"));
            }
        }
    }
}
```

`Writer` takes the request and depot, so it can add `instance` from `req.uri().path()` (template `problem.rs`).
`Scribe` (`fn render(self, res: &mut Response)`, no `async_trait`) is enough when it needs neither.

## RFC 9457 problem details (feature `rfc9457`)

`salvo::prelude::{Problem, PlainProblem, NoExtensions}`:

```rust
use serde::Serialize;

#[derive(Serialize)]
struct Fields {
    errors: std::collections::BTreeMap<String, Vec<String>>,
}

let problem = Problem::new(StatusCode::UNPROCESSABLE_ENTITY)   // type about:blank, title from the status
    .kind("https://example.com/problems/validation")           // the RFC's `type` member
    .title("Validation failed")
    .detail("one or more fields are invalid")
    .instance("/api/v1/todos")
    .with_extensions(Fields { errors });                        // typed extension members
res.render(problem);                                            // sets status and application/problem+json
```

- `Problem::new(status).extension("name", json!(..))` adds untyped members (`Problem<Map<String, Value>>`).
- Extensions must serialize to an object and may not reuse `type`, `title`, `status`, `detail` or `instance`
  (serialization fails, and a 500 is rendered instead).
- `PlainProblem::from(&status_error)` maps `name` to `title` and `brief` to `detail`.
- With `oapi`, `Problem<E>` implements `ToSchema` (component `Problem` for `PlainProblem`) and
  `EndpointOutRegister` (a `default` response).

## Catchers

The `Catcher` runs for every response whose status is 4xx/5xx and whose body is empty or a `StatusError`: unknown
paths (404), wrong methods (405), extractor failures (400), `StatusError` returns, middleware rejections, timeouts
(503), panics caught by `CatchPanic` (500). A body written by a handler is never replaced.

```rust
use salvo::catcher::{Catcher, ProblemGoal};

let service = Service::new(router).catcher(Catcher::new(ProblemGoal::new()));   // always problem+json
let service = Service::new(router).catcher(Catcher::default().hoop(on_404));     // default goal + your hoops
```

- `Catcher::default()` negotiates HTML, JSON, XML or text from `Accept`; fine for websites, wrong for APIs (an API
  client without `Accept` gets HTML).
- `ProblemGoal` always renders `application/problem+json` but leaves out `instance`. The template's
  `problem::catch` handler does the same with `instance` set to the request path; pass it as
  `Catcher::new(problem::catch)`.
- Catcher hoops run before the goal. A hoop that renders a body and calls `ctrl.skip_rest()` replaces the goal for
  that response:

```rust
#[handler]
async fn on_404(res: &mut Response, ctrl: &mut FlowCtrl) {
    if res.status_code == Some(StatusCode::NOT_FOUND) {
        res.render(Text::Html("<h1>Nothing here</h1>"));
        ctrl.skip_rest();
    }
}
```

## Panics

`salvo::catch_panic::CatchPanic` (feature `catch-panic`) turns a panic below it into a 500 for the catcher. Hoop it
first on the root router so it wraps everything. Without it the client's connection is dropped with no response
(the server keeps running).

## `anyhow` and `eyre`

With feature `anyhow` (or `eyre`) a handler may return `anyhow::Result<T>` (`eyre::Result<T>`); errors become 500.
Convenient for prototypes, but every failure is a 500 with no detail; APIs should map errors to statuses.