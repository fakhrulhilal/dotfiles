# Routing and HTTP

Upstream guides: `docs/router.md`, `docs/router/module.md`, `docs/router/path_param.md`, `docs/router/href.md`,
`docs/router/content.md`, `docs/router/error.md`, `docs/context/functions_not_middlewares.md`,
`docs/context/memoize.md`. Examples: `examples/module-router`, `examples/path-query-params`,
`examples/request-response`, `examples/error`, `examples/context`.

## Modules map to URLs

`module_router!()` maps the module that calls it to `/`. Each descendant module adds one kebab-case segment.

| Module                   | Route path          |
|--------------------------|---------------------|
| `app`                    | `/`                 |
| `app::blog_posts`        | `/blog-posts`       |
| `app::settings::profile` | `/settings/profile` |
| `app::_marketing::about` | `/about`            |

- The macro uses link-time discovery, not a filesystem scan. A route module must be reachable through `mod`.
- Two handlers in one module share a path, so they must serve different methods.
- A module whose name starts with `_` is a group: it scopes layouts and layers but adds no URL segment.
- `segment!(rename = "articles")` or `segment!(kind = Static | Group | Param | CatchAll)` overrides the segment. A
  rename is used as written, without kebab-casing.

## Handlers

```rust
use topcoat::{
    Result,
    context::Cx,
    router::{Body, Next, Slot, layer, layout, page, response::Response, route},
    view::{View, view},
};

#[page]                                   // GET at the module path
async fn settings(cx: &Cx) -> Result<impl View> {
    Ok(view! { <h1>"Settings"</h1> })
}

#[page(POST "./export")]                  // POST at <module path>/export
async fn export() -> Result<impl View> {
    Ok(view! { <p>"Export started"</p> })
}

#[route(GET)]                             // any response type, e.g. an API
async fn health() -> Result<&'static str> {
    Ok("ok")
}

#[layout]                                 // wraps pages in this module and below
async fn shell(slot: Slot<'_>) -> Result<impl View> {
    Ok(view! { <main>(slot)</main> })
}

#[layer]                                  // wraps handling for this module path as a prefix
async fn log(cx: &Cx, body: Body, next: Next<'_>) -> Result<Response> {
    let response = next.run(cx, body).await?;
    Ok(response)
}
```

- Methods: `#[route(POST)]`, `#[route([GET, POST])]` or `#[route(*)]`. A specific method beats `*`. GET also answers
  HEAD unless overridden.
- A path starting with `./` is joined to the module path. `"./"` serves the module path with a trailing slash.
  Trailing-slash mismatches redirect.
- An absolute path (`#[route(GET "/health")]`) ignores the module tree and must be registered with `.discover()`, or
  by name with `.route(handler)` / `.page(handler)`.
- Layouts and layers apply by logical path prefix. The least specific is outermost. Two discovered layouts, or two
  discovered layers, at the same path are rejected; register several layers explicitly with `RouterBuilder::layer` to
  give them an order.
- Only pathless layers wrap requests that match no route.
- A duplicate path and method panics when the router is built.

## Path and query parameters

```rust
// src/app/posts/post_id.rs serves /posts/{post_id}
use topcoat::{
    Result,
    context::Cx,
    router::{module_param, page, path_param, query_params},
    view::{View, view},
};

module_param!(pub post_id: u64, error = bad_request);   // declares the PostId type

#[query_params(error = bad_request)]
struct PostQuery {
    page: Option<u32>,
}

#[page]
pub async fn page(cx: &Cx) -> Result<impl View> {
    let post_id = path_param::<PostId>(cx)?;             // &u64
    let query = query_params::<PostQuery>(cx)?;          // &PostQuery
    Ok(view! { <h1>"Post " (post_id) ", page " (query.page.unwrap_or(1))</h1> })
}
```

- `module_param!(slug)` without a type reads a decoded `&str` and cannot fail, so there is no `?`.
- With a type but no `error`, the read returns `Result<&T, &<T as FromStr>::Err>`.
- The parameter name comes from the declaration, not the file name.
- One module holds one `module_param!` or one `segment!`, never both. Nest modules for several parameters. Descendant
  handlers can read ancestor parameters when the type is visible (hence `pub`).
- `module_param!(*path)` is a catch-all. It must be the last segment and matches at least one segment.
- `path_param!` makes the same declaration without touching the module segment, for handlers with explicit paths.
- `#[query_params]` derives `serde::Deserialize`. Use `Option<T>` for optional keys.
- Path and query parsing is memoized per request. Reading a parameter that the matched route lacks panics.

## Links

```rust
<a href=(href!(posts::post_id::page, PostId(42)))>"Post"</a>
<form method="post" action=(href!(sign_in))></form>
```

- Parameters are passed as their wrapper types, in path order.
- `.resolve(cx)` gives a string, for example for a redirect.
- `.query(serializable)`, `.fragment("id")`, `.is_current(cx)` (for `aria-current`), `.absolute()` (needs
  `.base_url(...)` on the router).
- Segments are encoded. Empty, `.`, `..` or mismatched parameters panic.
- External URLs and standalone fragments stay plain strings.

## Request content and responses

```rust
use serde::Deserialize;
use topcoat::{
    Result,
    context::Cx,
    router::{content::Form, error::{SeeOther, see_other}, href, route},
};

#[derive(Deserialize)]
struct SignIn {
    name: String,
}

#[route(POST)]
async fn sign_in(cx: &Cx, Form(form): Form<SignIn>) -> Result<SeeOther> {
    // validate `form`, then mutate
    Ok(see_other(href!(page).resolve(cx)))
}
```

- `router::content::{Form, Json}`. `Form` reads the query on GET/HEAD and the URL-encoded body otherwise.
- `Option<Form<T>>` allows missing input but still rejects malformed input.
- Buffered bodies are limited to 2 MiB. Raise it per prefix with `.layer(BodyLimit::max(bytes).at("/upload"))`. A raw
  `Body` bypasses the limit.
- A response tuple is: optional status, then headers or extensions, then the body last.
- A route that returns HTML uses `view.single().await?` (a `ViewHandle`) or a `BoxView<'static>` for streaming.
- Request accessors live in `router::request` (`headers(cx)`, `uri(cx)`). `client_ip(cx)` ignores forwarding headers
  unless `.trusted_proxies(...)` is configured.
- The default `OriginPolicy` rejects cross-origin mutations and WebSockets before layers run. It is not authentication.

## Errors, redirects, rewrites

```rust
use topcoat::router::error::RouterErrorExt;

let drink = Drink::filter_by_slug(slug).first().exec(&mut db(cx)).await?.ok_or_not_found()?;
```

- `RouterErrorExt` adds `ok_or_not_found()`, `ok_or_forbidden()`, `ok_or_unauthorized()` and friends to `Option` and
  `Result`.
- `see_other(url)` is 303 for Post/Redirect/Get. `redirect(url)` is 307 and replays the method.
- An unknown error becomes a 500 and its message is not sent to the client. A `bad_request` message is public.
- `error_boundary(fallback: |error| ..., (slot))` in a layout renders a branded error page. The fallback must emit a
  status itself, for example `(StatusCode::FORBIDDEN)`, otherwise the response is 200. Return `Err(error)` from the
  fallback to pass an error on.
- `not_found!();` adds a catch-all page so unmatched URLs under that path render through the layouts.
- `Err(rewrite(path, Body::empty()).method(Method::GET).with(value).into())` handles the request again at another path
  without a client round trip. It gets a fresh context and memoize cache and drops pending response changes. Read the
  carried value with `try_request_context::<T>(cx)`. `original_uri(cx)` sees what the client actually sent.

## Guards are functions

Do not authenticate in a layer and trust that it ran. Compose small functions over `cx` and call them wherever the
result is needed, including inside components.

```rust
use topcoat::{
    Result,
    context::{Cx, app_context, memoize},
    router::error::{RouterErrorExt, UnauthorizedError},
};

fn db(cx: &Cx) -> Db {
    app_context::<Db>(cx).clone()
}

#[memoize(as_ref)]                      // one lookup per request, callers get Option<&User>
async fn fetch_user(cx: &Cx, user_id: &str) -> Option<User> {
    User::fetch_by_id(user_id).exec(db(cx)).await
}

async fn fetch_current_user(cx: &Cx) -> Option<&User> {
    let user_id = session_user_id(cx)?;     // your own helper: Option<&str>
    fetch_user(cx, user_id).await
}

async fn require_auth(cx: &Cx) -> Result<&User, UnauthorizedError> {
    fetch_current_user(cx).await.ok_or_unauthorized()
}
```

`#[memoize]` rules: the function takes `cx`, arguments implement `Hash`, the output is owned and
`Send + Sync + 'static`, and callers receive `&T`. `#[memoize(as_ref)]` borrows the contents of an `Option` or `Result`.
Concurrent calls share one computation. Recursion with the same arguments panics.

## Context

- `.app_context(value)` registers one `Send + Sync + 'static` value per exact type. A duplicate type panics.
- `app_context::<T>(cx)` and `request_context::<T>(cx)` borrow and panic when the value is missing. The `try_*` forms
  return `Option`.
- `cx.with(value)` scopes a value to a subtree without changing the parent. Clone `Cx` to move it into a stream or
  task.