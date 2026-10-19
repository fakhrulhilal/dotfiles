# Builds, APIs, servers and deployment

Book chapters (`src/` of <https://github.com/leptos-rs/book>): `getting_started/`, `csr_wrapping_up.md`, `ssr/`, `server/`,
`progressive_enhancement/`, `islands.md`, `deployment/`, `web_sys.md`, `testing.md`. Templates:
<https://github.com/leptos-rs/start-trunk>, <https://github.com/leptos-rs/start-axum>.

## Pick a mode

| Mode | Build tool | Crate features | Use when |
|------|------------|----------------|----------|
| CSR  | Trunk | `leptos/csr` | Static hosting (GitHub Pages, any CDN), a separate JSON API, internal apps behind a login |
| SSR + hydration | cargo-leptos | `ssr` on the server binary, `hydrate` on the WASM lib | SEO, fast first paint, server functions instead of a hand-written API, works before WASM loads |
| Islands | cargo-leptos | `leptos/islands` on top of SSR | Mostly static pages with small interactive parts; smallest WASM |

## Client-side rendering with Trunk

```text
app/
├── Cargo.toml        leptos = { version = "0.8", features = ["csr"] }
├── Trunk.toml        optional: dev proxy, public URL
├── index.html        <link data-trunk rel="tailwind-css" href="tailwind.css"/> (see rust-ui.md)
└── src/main.rs       mount_to_body(App)
```

```sh
trunk serve --open                     # dev server with rebuild and reload on 127.0.0.1:8080
trunk serve --port 8081                # when 8080 is taken (e.g. by the API)
trunk build --release                  # static files in dist/
trunk build --release --public-url /repo-name/   # served under a sub path (GitHub project pages)
```

- Deploy `dist/` to any static host. For client-side routes, make unknown paths serve `index.html`: copy it to
  `404.html` on GitHub Pages, or add a `/* /index.html 200` rewrite on Netlify-style hosts.
- With `--public-url`, also set `<Router base="/repo-name">`.
- Use the size profile from "WASM size" below as the release profile.

## Calling a JSON API

A CSR front end (or the browser half of an SSR app) talks to a separate backend over HTTP. Keep all HTTP in one module
that returns user-ready errors, share the request and response types with the backend through a contract crate, and
let Leptos primitives track loading and pending state.

### Client choice

| Client | Targets | Futures | Use |
|--------|---------|---------|-----|
| `gloo-net` 0.6 (`default-features = false, features = ["http", "json"]`) | WASM only | `!Send` | CSR apps. Use 0.6: Leptos's router and server functions already depend on it, so 0.7 would ship a second copy. Panics with "cannot call wasm-bindgen imported functions on non-wasm targets" if it runs on the server |
| `reqwest` 0.13 (`features = ["json"]`; with `default-features = false` add a TLS feature such as `rustls` for native) | Native and WASM | `Send` on native | SSR apps whose resources call the API on both sides, server functions calling another service |

`!Send` futures need the local primitives: `LocalResource`, `Action::new_local`, `spawn_local`. A plain `Resource` or
`Action::new` will not compile with gloo-net.

### The API module

```rust
// src/api.rs: every call to the backend. Failures are messages ready to show to the user.
use gloo_net::http::{Request, Response};
use serde::{de::DeserializeOwned, Deserialize};
use todo_contract::{CreateTodo, StatusFilter, Todo};

/// A static site cannot read configuration at startup, so the base URL is fixed at build time:
/// `TODO_API_URL=https://api.example.com trunk build --release`. Empty means the site's own origin.
const API_URL: &str = match option_env!("TODO_API_URL") {
    Some(url) => url,
    None => "http://127.0.0.1:8080",
};

fn url(path: &str) -> String {
    format!("{}{path}", API_URL.trim_end_matches('/'))
}

pub async fn list(filter: StatusFilter) -> Result<Vec<Todo>, String> {
    json(send(Request::get(&url(&format!("/api/v1/todos?status={filter}"))).build()).await?).await
}

pub async fn create(input: &CreateTodo) -> Result<Todo, String> {
    json(send(Request::post(&url("/api/v1/todos")).json(input)).await?).await
}

async fn send(request: Result<Request, gloo_net::Error>) -> Result<Response, String> {
    let response = match request {
        Ok(request) => request.send().await,
        Err(error) => Err(error),
    }
    .map_err(|_| "The service is unreachable.".to_owned())?;
    if response.ok() { Ok(response) } else { Err(problem_message(response).await) }
}

async fn json<T: DeserializeOwned>(response: Response) -> Result<T, String> {
    response.json().await.map_err(|_| "The service sent an unexpected answer.".to_owned())
}

/// The parts of an RFC 9457 problem document shown to the user.
#[derive(Default, Deserialize)]
struct Problem { title: Option<String>, detail: Option<String> }

async fn problem_message(response: Response) -> String {
    let status = response.status();
    let problem: Problem = response.json().await.unwrap_or_default();
    problem.detail.or(problem.title).unwrap_or_else(|| format!("Request failed ({status})."))
}
```

### Loading and changing data

```rust
use leptos::prelude::*;
use leptos_router::hooks::query_signal;
use todo_contract::{CreateTodo, StatusFilter};

#[component]
fn TodoPage() -> impl IntoView {
    // the filter lives in the URL (?status=active), so reloads and shared links keep it
    let (status, set_status) = query_signal::<StatusFilter>("status");

    // mutations: dispatch from handlers; pending/value/version are signals
    let create = Action::new_local(|input: &CreateTodo| {
        let input = input.clone();
        async move { api::create(&input).await }
    });

    // loading: reruns when the filter changes or after each finished create
    let todos = LocalResource::new(move || {
        create.version().get();
        let status = status.get().unwrap_or_default();
        async move { api::list(status).await }
    });

    let title = RwSignal::new(String::new());
    let on_submit = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        create.dispatch(CreateTodo { title: title.get_untracked() });
        title.set(String::new());
    };

    // report failed mutations (an effect is right here: it syncs with the outside world)
    Effect::new(move |_| {
        if let Some(Err(message)) = create.value().get() {
            leptos::logging::warn!("{message}");      // or a Rust/UI toast: show_toast().error(message)
        }
    });

    view! {
        <form on:submit=on_submit>
            <input bind:value=title placeholder="What needs doing?"/>
            <button type="submit" disabled=move || create.pending().get()>"Add"</button>
        </form>
        <button on:click=move |_| set_status.set(Some(StatusFilter::Active))>"Active"</button>
        <Transition fallback=|| view! { <p>"Loading..."</p> }>
            {move || Suspend::new(async move {
                match todos.await {
                    Ok(items) => items
                        .into_iter()
                        .map(|todo| view! { <li>{todo.title}</li> })
                        .collect_view()
                        .into_any(),
                    Err(message) => view! { <p role="alert">{message}</p> }.into_any(),
                }
            })}
        </Transition>
    }
}
```

- `<Transition/>` keeps the old list on screen while it reloads; `<Suspense/>` would flash the fallback each time.
- Re-fetch after a mutation by reading the action's `version()` in the fetcher (as above), or call
  `todos.refetch()` when the action's value arrives. Update rows in place instead when latency matters: keep the
  list in a signal or store and patch it with the returned item.
- Optimistic UI: render `create.input().get()` as a pending row until `pending()` turns false.
- `query_signal::<T>` needs `T: FromStr + ToString`; a contract enum with `Display` and `FromStr` works directly.
- For per-row actions (toggle, rename, delete), create one `Action::new_local` per operation at the page level and
  dispatch with the row id, or use `MultiAction` when several submissions run at once.

### Caching with leptos-fetch

Plain resources reload every time their source changes. When several views show the same data (a list under
different filters, a detail page and its list), cache it with [leptos-fetch](https://github.com/zakstucke/leptos-fetch)
0.4 (the Leptos 0.8 line): one cache per app, de-duplicated requests, stale-while-revalidate, invalidation, and
direct writes into the cache.

```rust
use std::time::Duration;
use leptos_fetch::{QueryClient, QueryOptions, QueryScopeLocal};

// At the root: one cache for the app.
QueryClient::new().provide();

type TodoList = Result<Vec<Todo>, String>;

/// One cache entry per filter. The key type must be Hash + Clone (derive Hash on contract enums).
/// _Local variants because gloo-net futures are !Send.
fn list_query() -> QueryScopeLocal<StatusFilter, TodoList> {
    QueryScopeLocal::new(api::list)
        .with_options(QueryOptions::new().with_stale_time(Duration::from_secs(30)))
}

#[component]
fn TodoPage() -> impl IntoView {
    let client: QueryClient = expect_context();
    let filter = /* Memo<StatusFilter> from the query string */;
    let list = client.local_resource(list_query(), move || filter.get());   // a LocalResource

    // After a mutation, patch every cached list instead of refetching.
    let place = move |todo: &Todo| {
        for f in StatusFilter::VALUES {
            client.update_query(list_query(), f, |list: Option<&mut TodoList>| {
                let Some(Ok(items)) = list else { return };
                match (items.iter().position(|t| t.id == todo.id), f.matches(todo)) {
                    (Some(i), true) => items[i] = todo.clone(),
                    (Some(i), false) => drop(items.remove(i)),
                    (None, true) => items.push(todo.clone()),
                    (None, false) => {}
                }
            });
        }
    };

    view! {
        <Transition fallback=LoadingRows>
            // Read the resource synchronously so <For/> keeps unchanged rows; awaiting it in a
            // Suspend would rebuild the whole list on every cache write.
            <For each=move || list.get().and_then(Result::ok).unwrap_or_default()
                 key=|t| (t.id, t.updated_at) let(todo)>
                <TodoItem todo/>
            </For>
        </Transition>
    }
}
```

- `stale_time` (default never) decides when a cached value is refetched in the background the next time it is used;
  `gc_time` (default 5 minutes) drops unused entries. Within `stale_time`, changes made elsewhere (another tab,
  another app) are not seen; shorten it or call `client.invalidate_query(scope, key)` when that matters.
- Errors are cached like values when the query returns `Result`. Offer a Retry that calls `invalidate_query`.
- Scopes are identified by the function they wrap: build them from a named `fn` (as above), never from a fresh
  closure each time, or every call is a new cache.
- `update_query` returns early with `None` for lists never loaded; they load fresh when first shown.
- Key `<For/>` by id plus a version (`updated_at`) when rows are plain data: a changed row gets a fresh component.
- `QueryDevtools` (feature `devtools`) shows the cache in debug builds. With SSR, add `leptos-fetch/ssr` to the
  `ssr` feature and use the non-local `resource` so values stream from the server.

### Contract crate, CORS and the dev proxy

- Share request and response types with the backend in a plain crate (serde only). Gate server-only derives
  behind a feature (`#[cfg_attr(feature = "openapi", derive(ToSchema))]`) so the WASM build stays small.
  `uuid` needs its `js` feature only if the browser generates ids; `chrono` needs `wasmbind` only for `Utc::now()`.
- A front end on another origin needs the API to allow it through CORS (exact origins, not `*`, when credentials
  are sent). Cookies across origins also need `credentials: include` and `SameSite=None; Secure`.
- In development, avoid CORS with Trunk's proxy and an empty base URL:

  ```toml
  # Trunk.toml
  [[proxy]]
  backend = "http://127.0.0.1:8080/api/"   # /api/... on the Trunk server goes to the backend
  ```

- Retries, timeouts and auth headers belong in `api.rs`, never in components.

### Calling an API from an SSR app

Prefer server functions (below): they run on the server, can call the backend or the database directly, and keep
secrets off the client. If the browser must call the API itself, use `reqwest` with a `Resource` so the first load
happens on the server and is embedded in the HTML. The server may need a different base URL from the browser
(an internal host name), so pick it with `cfg!(feature = "ssr")`.

## Server-side rendering with cargo-leptos

```text
app/
├── Cargo.toml        [lib] crate-type = ["cdylib", "rlib"], features ssr/hydrate, [package.metadata.leptos]
├── src/lib.rs        pub mod app; #[cfg(feature = "hydrate")] pub fn hydrate()
├── src/main.rs       #[cfg(feature = "ssr")] Axum server
├── src/app.rs        shell() and App
├── style/main.scss   built to /pkg/<output-name>.css
├── public/           copied to the site root
└── end2end/          Playwright tests
```

```toml
[dependencies]
leptos = { version = "0.8" }
leptos_router = { version = "0.8" }
leptos_meta = { version = "0.8" }
leptos_axum = { version = "0.8", optional = true }
axum = { version = "0.8", optional = true }
tokio = { version = "1", features = ["rt-multi-thread"], optional = true }
sqlx = { version = "0.9", optional = true, features = ["runtime-tokio", "postgres"] }
console_error_panic_hook = { version = "0.1", optional = true }
wasm-bindgen = { version = "0.2", optional = true }

[features]
hydrate = ["leptos/hydrate", "dep:console_error_panic_hook", "dep:wasm-bindgen"]
ssr = ["dep:axum", "dep:tokio", "dep:leptos_axum", "dep:sqlx",
       "leptos/ssr", "leptos_meta/ssr", "leptos_router/ssr"]

[package.metadata.leptos]
output-name = "app"
site-root = "target/site"
site-pkg-dir = "pkg"
style-file = "style/main.scss"
assets-dir = "public"
site-addr = "127.0.0.1:3000"
reload-port = 3001
bin-features = ["ssr"]
bin-default-features = false
lib-features = ["hydrate"]
lib-default-features = false
lib-profile-release = "wasm-release"
```

```rust
// src/lib.rs
pub mod app;

#[cfg(feature = "hydrate")]
#[wasm_bindgen::prelude::wasm_bindgen]
pub fn hydrate() {
    console_error_panic_hook::set_once();
    leptos::mount::hydrate_body(app::App);
}

// src/app.rs
pub fn shell(options: LeptosOptions) -> impl IntoView {
    view! {
        <!DOCTYPE html>
        <html lang="en">
            <head>
                <meta charset="utf-8"/>
                <meta name="viewport" content="width=device-width, initial-scale=1"/>
                <AutoReload options=options.clone()/>
                <HydrationScripts options/>
                <MetaTags/>
            </head>
            <body><App/></body>
        </html>
    }
}

// src/main.rs
#[cfg(feature = "ssr")]
#[tokio::main]
async fn main() {
    use app::app::{shell, App};
    use leptos::prelude::*;
    use leptos_axum::{generate_route_list, LeptosRoutes};

    let conf = get_configuration(None).unwrap();          // reads [package.metadata.leptos] and LEPTOS_* env
    let options = conf.leptos_options;
    let addr = options.site_addr;
    let routes = generate_route_list(App);
    let pool = sqlx::PgPool::connect(&std::env::var("DATABASE_URL").unwrap()).await.unwrap();

    let router = axum::Router::new()
        .leptos_routes_with_context(
            &options,
            routes,
            move || provide_context(pool.clone()),        // available to components and server functions
            { let options = options.clone(); move || shell(options.clone()) },
        )
        .fallback(leptos_axum::file_and_error_handler(shell))
        .with_state(options);

    let listener = tokio::net::TcpListener::bind(&addr).await.unwrap();
    axum::serve(listener, router.into_make_service()).await.unwrap();
}

#[cfg(not(feature = "ssr"))]
pub fn main() {}
```

```sh
cargo leptos watch                 # dev: builds both halves, serves, reloads
cargo leptos build --release       # target/release/<bin> + target/site/
cargo leptos serve --release
cargo leptos end-to-end            # runs end2end-cmd in end2end-dir
```

At runtime the binary needs `LEPTOS_SITE_ROOT` (the copied `target/site`), `LEPTOS_SITE_ADDR`
(`0.0.0.0:8080` in a container) and, if it reads config from it, `Cargo.toml` (or set `LEPTOS_OUTPUT_NAME`).
Plain `leptos_routes(&options, routes, shell)` is enough when no context is needed. Other Axum routes (health checks,
a JSON API) can be added to the same router.

### The life of a page load

1. The server renders `App` for the URL to HTML (resources start loading on the server) and streams it.
2. The browser shows the HTML, then loads JS and WASM and hydrates: it walks the existing DOM, attaching listeners
   and reactivity instead of re-creating nodes. Clicks before hydration do nothing.
3. Later navigations are client-side; the app behaves like a SPA.

Everything in components therefore runs on both sides. `App` also runs once at server start to collect routes.

## Server functions

```rust
use leptos::prelude::*;

#[server]
pub async fn add_todo(title: String) -> Result<Todo, ServerFnError> {
    use sqlx::PgPool;                           // server-only imports go inside the body
    let user = require_user().await?;           // authorize every call
    let title = title.trim();
    if title.is_empty() || title.chars().count() > 200 {
        return Err(ServerFnError::new("Title must be 1 to 200 characters."));
    }
    let pool = expect_context::<PgPool>();
    let todo = sqlx::query_as!(Todo, "insert into todos (owner, title) values ($1, $2) returning *", user.id, title)
        .fetch_one(&pool)
        .await
        .map_err(|e| ServerFnError::new(e.to_string()))?;
    Ok(todo)
}
```

- On the server the body runs; in the browser it becomes an HTTP call to `/api/<name><hash>`. Use it from
  `Resource::new(..., |_| list_todos())`, `ServerAction::<AddTodo>::new()`, or `spawn_local(async { add_todo(t).await })`.
- Top-level `async fn` only, not generic, arguments and result serializable, result `Result<T, E>`.
- Options: `#[server(prefix = "/api", endpoint = "add_todo", input = Json, output = Json)]`. The default input is
  `PostUrl` (form-compatible), output `Json`. `GetUrl` makes it a cacheable GET. `#[server(default)]` on an
  argument fixes empty `Vec`s under URL encoding.
- Custom errors: any `E: FromServerFnError` (serde type with `type Encoder = JsonEncoding` and a
  `from_server_fn_error(ServerFnErrorErr) -> Self`).
- Use `i32`/`i64`, not `usize` (WASM is 32-bit). `Option` fields and tuple enum variants misbehave with URL
  encoding; use `input = Json` for those.
- Server functions are public endpoints: authenticate, authorize and validate inside each one, and never return
  more than the caller may see.

### Extractors, responses, redirects

```rust
#[server]
pub async fn whoami() -> Result<String, ServerFnError> {
    use axum::http::HeaderMap;
    let headers: HeaderMap = leptos_axum::extract().await?;      // any Axum extractor with () state
    // extract_with_state(&state) for extractors needing State (state provided through context)
    let response = expect_context::<leptos_axum::ResponseOptions>();
    response.set_status(axum::http::StatusCode::ACCEPTED);
    response.insert_header(axum::http::header::SET_COOKIE, "seen=1; Path=/; HttpOnly".parse()?);
    Ok(format!("{:?}", headers.get("user-agent")))
}

#[server]
pub async fn login(username: String, password: String) -> Result<(), ServerFnError> {
    /* verify */
    leptos_axum::redirect("/");          // real redirect without JS; client-side navigation with the router
    Ok(())
}
```

Provide shared server state (pools, settings) through `leptos_routes_with_context` and read it with
`expect_context`. Server functions called over HTTP only see that context, not the context the app's components set up.

## Progressive enhancement: `<ActionForm/>`

```rust
#[component]
fn AddTodo() -> impl IntoView {
    let add = ServerAction::<AddTodo>::new();
    let failed = move || add.value().with(|v| matches!(v, Some(Err(_))));
    view! {
        <ActionForm action=add>
            <input type="text" name="title"/>          // name matches the argument
            <button type="submit" disabled=move || add.pending().get()>"Add"</button>
        </ActionForm>
        <Show when=failed>"Could not add the todo."</Show>
    }
}
```

- Works without WASM (a normal POST, then a redirect back) and without reload once hydrated.
- Only with the default URL encoding. Nested struct arguments use `name="arg[field][sub]"`.
- Client-side validation: `on:submit:capture=move |ev| { if AddTodo::from_event(&ev).is_err() { ev.prevent_default() } }`
  (capture runs before the form's own handler).
- Reload lists after an action by reading `add.version().get()` in the resource source.
- `MultiActionForm` with `ServerMultiAction` for several submissions in flight.

## SSR modes and blocking resources

| Mode | Behaviour |
|------|-----------|
| `SsrMode::OutOfOrder` (default) | Send the shell with fallbacks at once, stream each `<Suspense/>` as it resolves (needs a little inline JS) |
| `SsrMode::PartiallyBlocked` | Like out-of-order, but `<Suspense/>`s reading a blocking resource are resolved into the initial HTML |
| `SsrMode::InOrder` | Stream top-down, pausing at each `<Suspense/>`; no fallbacks |
| `SsrMode::Async` | Wait for all data, then send the full page (data-dependent `<title>` and meta) |

Set per route: `<Route path=path!("/post/:id") view=Post ssr=SsrMode::Async/>`. The strictest mode of the matched
nested routes wins. `Resource::new_blocking` holds the stream until a `<Suspense/>` reading it resolves, so
`<Title/>`/`<Meta/>` from that data reach the `<head>`.

## Hydration bugs

| Symptom | Cause | Fix |
|---------|-------|-----|
| Panic `Option::unwrap()` on `None` in tachys, or "expected a text node" | Server and client rendered different trees | No `cfg!(target_arch)` or time/random-dependent output; data comes from resources |
| Mismatch near a `<p>` | Invalid HTML the browser repaired (`<div>` in `<p>`, nested `<a>`, `<form>` in `<form>`) | Fix the HTML; run it through a validator |
| Mismatch inside a table | Browser inserted `<tbody>` | Always write `<tbody>` (and `<thead>`) |
| `cannot call wasm-bindgen imported functions on non-wasm targets` | Browser-only crate used during SSR | Move it into `Effect::new`, use a cross-platform crate, or `#[cfg(feature = "hydrate")]` |
| Errors about `mio`, `tokio` or missing `core` items in the WASM build | Server crate compiled to WASM | `optional = true` and enable it only in `ssr` |
| Signal set during SSR but not after hydration | Side effect inside a resource fetcher or `Suspend` | Return the value and await it instead |

## Where async code runs

| | During SSR | During hydration | Later in the browser |
|--|-----------|------------------|----------------------|
| `Resource` | Runs; result serialized into the page | Not run; value read from the page | Runs when its source changes |
| `LocalResource` | Not run | Runs once after hydration | Runs when tracked signals change |
| `Suspend` | Runs | Runs again to hydrate | Runs when needed |
| Server function | Called directly, with the app's context | Not run if used inside a resource | HTTP call to its endpoint |
| `Effect` | Not run | Runs | Runs |

## Islands

Enable `leptos = { features = ["islands"] }`, call `leptos::mount::hydrate_islands()` in `hydrate()`, and use
`<HydrationScripts options islands=true/>`. Everything is static HTML except components marked `#[island]`.

- Only island code is compiled to WASM; plain `#[component]`s run only on the server (unless used inside an island)
  and may call server-only code directly.
- Island props must be serializable (they are written into a `data-props` attribute; add `serde`).
- Pass server-rendered content into an island as `children`; it is not hydrated or shipped as WASM.
- Islands share state through context provided by an enclosing island.
- There is no client-side router. Use the `islands_router` example's approach, and `set_redirect_hook` so server
  function redirects navigate.

## Deployment

- Always build and test in release mode; behaviour can differ from debug.
- Container: build with `cargo leptos build --release`, copy `target/release/<bin>` and `target/site/` into a
  small runtime image, set `LEPTOS_SITE_ROOT=site`, `LEPTOS_SITE_ADDR=0.0.0.0:8080`. See the `docker-container`
  skill for the image conventions.
- Put a reverse proxy (Caddy, Nginx, Traefik) in front for TLS, compression and headers.
- Serverless (Lambda via `start-aws`, Deno/Cloudflare via `leptos_axum` `wasm` feature) restricts local state and
  requires every server crate to compile to WASM.
- Sub path: `<Router base="/my-app">`, `<HydrationScripts root="/my-app"/>`, and `set_server_url` for server
  functions mounted elsewhere.

### WASM size

```toml
[profile.wasm-release]          # cargo-leptos: lib-profile-release = "wasm-release"; CSR: use as [profile.release]
inherits = "release"
opt-level = "z"
lto = true
codegen-units = 1
panic = "abort"
```

- Serve WASM compressed (it shrinks to under half).
- Avoid heavy crates in the client: `regex` with default features adds about 500 KB; generic components with large
  bodies are copied per type.
- `leptos_server` features `miniserde` / `serde-lite` add smaller `Resource::new_*` encodings.
- Code splitting: `cargo leptos watch --split` with `#[lazy]` functions and `#[lazy_route]` routes.
- Islands cut WASM roughly in half for content-heavy apps.
