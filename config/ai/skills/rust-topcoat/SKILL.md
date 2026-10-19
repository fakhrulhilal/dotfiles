---
name: topcoat
description: "Build server-rendered Rust web apps with Topcoat (tokio-rs/topcoat): module routing, the view! macro and #[component], signals and $(...) runtime expressions, #[procedure] and #[shard], live! streaming, assets, Tailwind, cookies and sessions. Use when learning Topcoat, starting an app with `topcoat new`, or writing or reviewing code that imports the `topcoat` crate (module_router!, #[page], #[route], #[layout], view!, href!, signal). Not for contributing to the Topcoat framework repository itself, which ships its own check/style/commit skills."
license: MIT
---

# Topcoat

Topcoat is an experimental Rust framework for server-rendered web apps. Async components produce HTML on the server.
Browser interactivity comes from runtime expressions that compile to both Rust and JavaScript, so there is no client
WASM bundle. Apps depend only on the `topcoat` facade crate.

Written against Topcoat 0.10.0. The API still changes between releases, so check the installed version in `Cargo.lock`
and confirm anything surprising against the upstream sources below before trusting this skill.

## Where to look

| Need                         | Source                                                                     |
|------------------------------|----------------------------------------------------------------------------|
| Dense, current API summary   | `llms.txt` at the repo root                                                |
| Full guides                  | `docs/` (`router/`, `view/`, `runtime/`, `context/`, one file per service) |
| Runnable single-feature apps | `examples/<feature>/src/`                                                  |
| A complete app               | `demos/coffee-shop/`                                                       |
| API reference                | <https://docs.rs/topcoat>                                                  |

Upstream is <https://github.com/tokio-rs/topcoat>. A local checkout, when present, lives at `~/Proyek/topcoat`; prefer
reading it over fetching, and `git -C ~/Proyek/topcoat log -1` tells you how fresh it is.

Read the reference that matches the task before writing code:

- [references/routing.md](references/routing.md): modules to URLs, handlers, parameters, links, forms, errors, guards.
- [references/views.md](references/views.md): `view!` syntax, components, attributes, keys, streaming with `live!`.
- [references/runtime.md](references/runtime.md): signals, `$(...)`, events, procedures, shards, navigation.
- [references/services.md](references/services.md): CLI, features, assets, Tailwind, fonts, icons, UI, cookies,
  sessions, mail and the optional HTTP integrations.

## Start an app

```sh
cargo install topcoat-cli
topcoat new app --recommended     # module routing, runtime, Tailwind, UI, icons, fonts, Toasty/SQLite todo
cd app
cargo run -- toasty migration generate && cargo run -- toasty migration apply   # only with database models
topcoat dev                       # build, bundle assets, reload on change; http://127.0.0.1:3000
```

`--minimal` gives a small app without a database, and no preset starts the interactive wizard. The destination must not
exist. `HOST` and `PORT` override the bind address. Run `topcoat fmt` to format macro bodies (`--rustfmt` runs rustfmt
first, `--check` for CI).

## Shape of an app

```rust
// src/main.rs
mod app;

#[tokio::main]
async fn main() {
    topcoat::start(app::router()).await.unwrap();
}
```

```rust
// src/app.rs: the module that calls module_router!() maps to "/"
mod menu; // src/app/menu.rs serves /menu. Routes exist only through `mod`, never by scanning files.

use topcoat::{
    Result,
    asset::{AssetBundle, RouterBuilderAssetExt},
    context::Cx,
    router::{Router, RouterBuilderDiscoverExt, Slot, href, layout, module_router, page},
    runtime::RouterBuilderRuntimeExt,
    view::{View, view},
};

pub fn router() -> Router {
    module_router!()
        .discover()                            // absolute-path handlers, procedures, shards, fonts
        .assets(AssetBundle::load().unwrap())  // needed by the runtime script, Tailwind, asset!()
        .runtime()                             // register application layers before this
        .build()
}

#[layout]
async fn shell(slot: Slot<'_>) -> Result<impl View> {
    Ok(view! {
        <!DOCTYPE html>
        <html>
            <head>
                <title>"App"</title>
                topcoat::dev::script()
                topcoat::runtime::script()
            </head>
            <body>
                <nav><a href=(href!(menu::page))>"Menu"</a></nav>
                <main>(slot)</main>
            </body>
        </html>
    })
}

#[page]
pub async fn page(cx: &Cx) -> Result<impl View> {
    Ok(view! { <h1>"Home"</h1> })
}
```

## Rules that prevent most mistakes

Routing:

- The URL comes from the module path, in kebab-case. Function names never affect it. A missing `mod` line means a
  missing route.
- `#[page]` serves GET and returns `Result<impl View>`. `#[route(POST)]` returns `Result<T>` for any response type.
  Both take an optional `cx: &Cx` and at most one extractor argument such as `Form(input): Form<Input>`.
- Build every internal URL with `href!(handler, Param(value))`. Never hand-write an internal path string.
- Mutations use POST and finish with `Ok(see_other(href!(page).resolve(cx)))` (Post/Redirect/Get).
- Import the `RouterBuilder*Ext` trait for each builder method (`.discover()`, `.assets()`, `.runtime()`,
  `.cookies()`). A Cargo feature only exposes an API; the builder call installs the service.

Views:

- Text is quoted (`"Hello"`), Rust values are interpolated with `(expr)`, and values are escaped.
- Void elements (`<br>`, `<input>`, `<img>`) take no closing tag. Every other element needs one.
- A view is lazy and captures by move, like `async move`. Clone a value first if it is needed after the `view!`.
- Components in one view render concurrently and their run order is unspecified. Do ordered side effects before
  building the view.
- `disabled=(bool)` and `title=(Option<T>)` omit the attribute when false or `None`. A literal `disabled="false"` is
  still present. ARIA and other enumerated attributes need the strings `"true"`/`"false"`.
- Add `#[key(item.id)]` above a `for` loop whose body holds stateful components, shards or live regions.

Request logic:

- Write guards as plain functions over `cx: &Cx` (`require_auth(cx).await?`) and call them where the result is
  needed. Do not put authentication in a layer and assume it ran.
- Share expensive per-request work with `#[memoize]`. Share app state with `.app_context(value)` and read it with
  `app_context::<T>(cx)`.
- Turn `Option`/`Result` into HTTP errors with `RouterErrorExt`: `.ok_or_not_found()?`, `.ok_or_forbidden()?`.

Runtime and security:

- Everything the browser sends back is user input: signal values restored on the server, procedure arguments and
  shard arguments. Validate all of it.
- Procedures and shards have their own endpoints, so page and layout guards do not protect them. Authorize inside
  every `#[procedure]` and `#[shard]`.
- A `$(...)` expression sends its captured values to the browser, including private fields of a `#[record]`. Never
  capture a secret.
- `$(...)` accepts a limited subset of Rust. Unsuffixed integer literals are `usize`, and arithmetic panics on
  overflow. Use `raw!` only when the vocabulary falls short.
- Cookies, status and headers must be set before the response starts streaming.

## Working method

1. Check the version and enabled features in `Cargo.toml`. The default features cover router, view, runtime, serve,
   discover, asset, compression, cookie, session, font and icon. `tailwind`, `ui`, `mail`, `sse`, `websocket`,
   `multipart`, `htmx`, `datastar`, `alpine-ajax`, `tower`, `fs` and `sitemap` are opt-in; `full` enables everything.
2. Find the closest `examples/<feature>` and copy its shape instead of inventing an API.
3. Make the change, then run `cargo check` and `topcoat fmt`. Macro errors point at the offending markup, so read
   them at the reported span.
4. Verify in the browser with `topcoat dev`. A plain `cargo run` works only when no asset bundle is required, or
   after `topcoat asset bundle` with the matching profile.

## Learning path

When someone is learning Topcoat rather than shipping a change, walk these in order and have them run each example:

1. `examples/hello-world`, then `docs/getting_started.md`.
2. `examples/module-router` and `examples/path-query-params` with `docs/router/module.md`.
3. `docs/view/view.md` and `docs/view/component.md`.
4. `examples/request-response` and `examples/error` with `docs/router/content.md` and `docs/router/error.md`.
5. `examples/context` and `examples/app-context` with `docs/context/functions_not_middlewares.md`.
6. `examples/runtime` with `docs/runtime.md`, then `docs/runtime/shard.md` and `docs/runtime/procedure.md`.
7. `examples/suspense` and `examples/live` with `docs/view/live.md`.
8. `examples/tailwind`, `examples/ui`, `examples/cookie`, `examples/session`.
9. `examples/toasty-todo` and `demos/coffee-shop` to see everything together.

Explain each step by the concept it adds, then give a small exercise that changes the example (add a route, add a
prop, move state into a shard) so the idea is used, not only read.