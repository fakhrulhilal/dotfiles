---
name: rust-sycamore
description: "Build Rust web front ends with Sycamore 0.9, the fine-grained reactive WASM framework: the view! macro (elements, attributes, aria-*/custom attributes, prop:, bind:value/checked, on:event, r#ref, attribute spread, bare if/match and (...) interpolation, which parts are reactive), #[component] and #[component(inline_props)], Props, Children, MaybeDyn, Keyed and Indexed lists, Show, signals (create_signal, get/get_clone/with/set/update/set_silent, untracked reads), create_memo vs create_selector, create_effect, on(), batch, scopes and disposal (on_cleanup, on_mount, create_root), context, NodeRef, spawn_local_scoped, resources (create_client_resource, create_isomorphic_resource), Suspense, Transition and async components, sycamore-router (Route derive, Router with HistoryIntegration, use_search_query, navigate, rel=external, StaticRouter), calling a JSON API with gloo-net, Trunk builds and WASM size, SSR with render_to_string, render_to_string_stream and hydrate. Use when learning Sycamore, starting a Sycamore project, or writing, reviewing or debugging code that imports `sycamore` or `sycamore_router` (view!, create_signal, Signal, ReadSignal, Keyed, Router, Route, use_search_query, spawn_local_scoped, create_client_resource, Suspense), including 'signal was disposed' panics, an app that stops reacting after a panic, views that never update or update too often, filters in the query string that ignore Back, and view! attribute errors."
license: MIT
---

# Sycamore

Sycamore is a fine-grained reactive Rust framework for web apps, in the style of SolidJS. A component is a function
that runs **once** and returns a `View`; afterwards only signals, and the closures the `view!` macro builds around
expressions that read them, update the DOM. It runs as a client-side WASM app built with Trunk, or rendered on the
server (`render_to_string`, streaming) and hydrated in the browser.

Written against **Sycamore 0.9.4** and **sycamore-router 0.9.4** (released 2026-10-06, MSRV 1.94) on Rust 1.99,
edition 2024. The `main` branch is 0.10 development and already changes the syntax (`on_click=` instead of
`on:click=`, components returning builders), so docs and examples from `main` may not compile on 0.9. Check
`Cargo.lock` first, and confirm anything surprising against the 0.9.4 sources below before trusting this skill.

## Versions that work together

| Crate               | Version | Notes                                                                          |
|---------------------|---------|--------------------------------------------------------------------------------|
| `sycamore`          | 0.9.4   | Features: `suspense` (resources, `Suspense`, `sycamore::futures`), `hydrate`, `serde` |
| `sycamore-router`   | 0.9.4   | Major/minor always matches `sycamore`                                          |
| `gloo-net`          | 0.7     | HTTP from the browser, `default-features = false, features = ["http", "json"]` |
| `web-sys`           | 0.3     | Enable each browser type used directly (`HtmlInputElement`, `UrlSearchParams`, ...) |
| `console_error_panic_hook` | 0.1 | Panics show a Rust message in the browser console                         |
| `trunk`             | 0.21 or 0.22 | Build tool; downloads `wasm-bindgen` and `wasm-opt`                       |

`js-sys` and `wasm-bindgen` are re-exported as `sycamore::web::{js_sys, wasm_bindgen}`; `web_sys` is not, so add
it yourself with the features you use.

## Where to look

| Need                     | Source                                                                                     |
|--------------------------|--------------------------------------------------------------------------------------------|
| Concepts, in order       | The book, rendered at <https://sycamore.dev/book/introduction>; markdown in `docs/next/` of <https://github.com/sycamore-rs/sycamore> at tag `0.9.4` |
| API reference            | <https://docs.rs/sycamore/0.9.4>, <https://docs.rs/sycamore-router/0.9.4>                  |
| Runnable examples        | `examples/` at tag `0.9.4`: `todomvc`, `http-request`, `router`, `iteration`, `context`, `attributes-passthrough`, `hydrate`, `ssr`, `ssr-streaming`, `ssr-suspense`, `transitions`, `js-snippets` |
| What the macro accepts   | `packages/sycamore-macro/tests/view/*-pass.rs` (and `*-fail.stderr` for the errors)        |
| What is reactive in `view!` | `is_dyn` in `packages/sycamore-view-parser/src/codegen.rs`                              |
| Starter project          | <https://github.com/sycamore-rs/start>                                                     |

The book is short and several pages are stubs (testing, deploy, the reactive graph, the Props macro). For anything
beyond the basics, read the crate source: it is small, and `~/.cargo/registry/src/*/sycamore-*-0.9.4/` has it once
the project has been built. A shallow clone into a scratch folder works too:
`git clone --depth 1 --branch 0.9.4 https://github.com/sycamore-rs/sycamore`.

Read the reference that matches the task before writing code:

- [references/views.md](references/views.md): `view!` syntax, attributes, `prop:`, `bind:`, events, node refs,
  control flow and what becomes reactive, components and props, children, `MaybeDyn`, attribute passthrough,
  `Keyed`/`Indexed`, `Show`.
- [references/reactivity.md](references/reactivity.md): signals and their methods, memos and selectors, effects and
  `on()`, `batch`, `untrack`, scopes and disposal, `on_cleanup`/`on_mount`, context, reducers, tweened signals.
- [references/async-and-api.md](references/async-and-api.md): `spawn_local_scoped`, resources, `Suspense`,
  `Transition`, async components, a JSON API client with RFC 9457 errors, the load-in-an-effect and
  mutate-then-place patterns, DOM access and browser events.
- [references/routing.md](references/routing.md): `#[derive(Route)]` syntax, `Router`, links and `rel="external"`,
  `navigate`, query strings (and the Back-button gap), hash, base path, nested routes, `StaticRouter`.
- [references/build-ssr-deploy.md](references/build-ssr-deploy.md): Trunk setup, styling, WASM size, static hosting
  and Docker, SSR modes, hydration, `NoSsr`/`NoHydrate`, `is_ssr!`, and testing.
- [references/pitfalls.md](references/pitfalls.md): errors and runtime failures met in a real app, each with its
  cause and fix.

## Start a project

```sh
rustup target add wasm32-unknown-unknown
cargo install --locked trunk
cargo new app && cd app
cargo add sycamore@0.9.4 --features suspense
cargo add console_error_panic_hook
trunk serve --open                          # http://127.0.0.1:8080, rebuilds on save
```

```html
<!-- index.html, next to Cargo.toml -->
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="color-scheme" content="light dark" />
    <title>App</title>
    <link data-trunk rel="css" href="assets/app.css" />
    <!-- wasm-release: the size profile in the root Cargo.toml, see references/build-ssr-deploy.md -->
    <link data-trunk rel="rust" data-bin="app" data-wasm-opt="z"
      data-cargo-profile-release="wasm-release" />
  </head>
  <body></body>
</html>
```

```rust
// src/main.rs
use sycamore::prelude::*;

fn main() {
    console_error_panic_hook::set_once();
    sycamore::render(App);
}

#[component]
fn App() -> View {
    let mut count = create_signal(0);
    let double = create_memo(move || count.get() * 2);
    view! {
        button(on:click=move |_| count += 1) { "Clicks: " (count) }
        p { "Double: " (double) }
        if count.get() > 3 {
            p { "That is a lot of clicks." }
        }
    }
}
```

## Rules that prevent most mistakes

Reactivity:

- A component body runs once. `(count)` (a signal) and `(count.get() * 2)` (a method call, which the macro wraps in
  a closure) update; `(value)` where `value` is a plain variable renders once. See the rule in
  [references/views.md](references/views.md#what-becomes-reactive).
- `Signal<T>` and `ReadSignal<T>` are `Copy` handles into an arena, for any `T`. Pass them into closures freely; put
  `String`s and other non-`Copy` values you need in several closures into a signal rather than cloning them around.
- `.get()` needs `T: Copy`; use `.get_clone()` or `.with(|v| ...)` otherwise. Inside event handlers and async code
  read with `.get_untracked()`/`.with_untracked()`, which do not subscribe anything.
- `create_memo` notifies its dependents every time it recomputes, even to an equal value. Use `create_selector`
  (needs `PartialEq`) when downstream work should run only on a real change.
- A signal belongs to the scope that created it and is disposed with it. Reading or writing it afterwards panics
  with "signal was disposed", and **a panic in a WASM app stops all reactivity**: the page stays on screen but
  nothing updates. In an async handler, finish with the call that may dispose the current scope (removing or
  replacing a list row), never touch the row's signals after it.
- An effect disposes what its previous run created, including tasks from `spawn_local_scoped`. Starting a fetch in
  an effect therefore cancels the stale one when its inputs change.
- Async code is not tracked. Make dependencies explicit with `on(signal, move || async move { ... })` for
  resources, or read them synchronously at the start of the effect.

Views:

- Write views with the `view!` DSL only, never the builder API (`div().children(...)`), even where the book or
  an example shows both.
- Elements are `tag(attr=value, ...) { children }`; text is a string literal; Rust expressions go in `(...)`.
- Attributes are type-checked per element: `disabled` takes `bool`, `maxlength` a string. `aria-*`, `data-*` and
  other dashed names are accepted as strings or `Option`s (`None` removes the attribute); quote a name that is not
  HTML (`"x-foo"="1"`).
- Use `bind:value=signal` (a `Signal<String>`) for inputs, `bind:checked` for checkboxes. The `value` attribute only
  sets the initial value; for a property without an attribute use `prop:name=`.
- `if`, `if let` and `match` can be written bare inside `view!` (since 0.9.4); branches are view nodes, so no inner
  `view! {}` is needed. A branch that must run Rust first is `=> ({ let x = ...; view! { ... } })`.
- `Keyed(list=..., view=..., key=...)` reuses a row while its key stays the same and never re-renders it with new
  data. Key by id **and** version (`(todo.id, todo.updated_at)`) to rebuild a changed row, or make the changing
  fields signals.
- `Show`, `Suspense`, `Transition`, `NoSsr`, `NoHydrate` and `Portal` live in `sycamore::web`, not the prelude.
- `autofocus` on `input` does not compile in 0.9.4 (declared twice). Focus through a `NodeRef` in `on_mount`.

Routing and API calls:

- Links are plain `a(href=...)`; the router intercepts same-origin clicks. Add `rel="external"` for anything the
  server serves (API docs, downloads, another app on the same host).
- Keep filters and other page state in the query string, read with `use_search_query`. In 0.9.4 Back and Forward
  between two addresses that differ only in the query do not update it; listen to `popstate` as well (code in
  [references/routing.md](references/routing.md#query-strings)).
- Keep every HTTP call in one `api.rs` module that returns `Result<T, String>` with messages ready for the user,
  including the field errors of RFC 9457 problem documents. Read the API base URL at compile time with
  `option_env!`, since a static site has no runtime configuration.

## Working method

1. Check `Cargo.toml`: the Sycamore version (0.9 vs 0.10 syntax), whether `suspense` and `hydrate` are on, and the
   `web-sys` features. Match the API to what is installed.
2. Find the closest book chapter, example or macro test at tag `0.9.4` and copy its shape.
3. Make the change. `cargo check --target wasm32-unknown-unknown` (and `cargo clippy` the same way). A plain
   `cargo check` builds for the host, where SSR code paths are compiled instead.
4. `cargo fmt`. rustfmt leaves `view!` bodies alone; `sycamorefmt` exists upstream but is not required.
5. Run it (`trunk serve`) and **read the browser console**: panics only show there, and after one the app is
   dead until reloaded. `console_error_panic_hook::set_once()` gives the Rust file and line.
6. Exercise the flows that dispose things: delete a row, change a row (if keyed by version), switch filters
   while a request is in flight, Back and Forward. Then check the console again.

## Testing

The book's testing page is a stub. What works:

- Pull logic out of components into plain functions and types and test them with ordinary `#[test]`s. Reactive
  code can be tested natively inside `create_root(|| { ... })`, as `sycamore-reactive`'s own tests do.
- Render to a string natively with `sycamore::render_to_string(App)` and assert on the HTML for static output.
- Test behaviour end to end in a real browser with Playwright, locating elements by role and label.

## Learning path

For someone learning rather than shipping a change, walk the book in this order and type each example into a
Trunk project:

1. Introduction: your first app, adding state, rendering lists, the todo app.
2. Guide: view DSL, data binding, node refs, contexts, attribute passthrough. Skip the builder API chapter.
3. Async resources and suspense, then an async component (`examples/http-request`).
4. Router, including query strings (`examples/router`).
5. Server-side rendering and streaming (`examples/ssr-streaming`), then hydration (`examples/hydrate`).
6. Cookbook: styling, WASM size.

After each step give a small exercise that changes the example (turn a memo into a selector and observe an effect
running less, key a list by version, move a filter into the URL, add a Retry to a failed load) so the idea is used,
not only read.
