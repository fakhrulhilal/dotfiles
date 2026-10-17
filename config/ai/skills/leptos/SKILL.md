---
name: leptos
description: "Build Rust web front ends and full-stack apps with Leptos 0.8, styled with Tailwind CSS v4 and Rust/UI components (rust-ui.com, shadcn-style, copied into the project): view! macro, #[component] and props, signals, memos, effects, stores, context, resources, Suspense, actions, leptos_router, leptos_meta, server functions, SSR and hydration with cargo-leptos and Axum, CSR with Trunk, calling a JSON API with gloo-net and leptos-fetch, islands, and Rust/UI components (Button, Input, Checkbox, Card, Alert, Dialog, Toast, Form) with the leptos_ui clx!/variants! macros and tw_merge. Use when learning Leptos, starting a project with Trunk or `cargo leptos new`, adding Tailwind or Rust/UI components (`ui init`, `ui add`), or writing, reviewing or debugging code that imports `leptos`, `leptos_router`, `leptos_meta`, `leptos_axum`, `reactive_stores`, `leptos_fetch`, `leptos_ui` or `tw_merge` (view!, signal, RwSignal, Resource, #[server], <Routes/>, QueryClient, variants!), including hydration errors and WASM build or size problems."
license: MIT
---

# Leptos

Leptos is a fine-grained reactive Rust web framework. Components are setup functions that run once; only signals and
closures passed into the view update later. It runs as a client-side WASM app (CSR, built with Trunk) or as a
server-rendered app that hydrates in the browser (SSR, built with `cargo-leptos` on Axum or Actix). The house style
for UI is Tailwind CSS v4 with [Rust/UI](https://www.rust-ui.com) components: shadcn-style source copied into the
project, not a component crate.

Written against Leptos 0.8.22 on stable Rust. Leptos 0.9 is in release candidates; stay on 0.8 until it is stable
and the Rust/UI helper crates and leptos-fetch support it. Check `Cargo.lock` first, and confirm anything surprising
against the sources below before trusting this skill.

## Versions that work together

| Crate                         | Version      | Notes                                                                      |
|-------------------------------|--------------|----------------------------------------------------------------------------|
| `leptos`, `leptos_router`     | 0.8          | 0.8.22, router 0.8.17                                                      |
| `leptos_meta`, `leptos_axum`  | 0.8          | 0.8.7, 0.8.10; `leptos_axum` pairs with axum 0.8                            |
| `reactive_stores`             | 0.4          | Same `reactive_graph` 0.2 as Leptos 0.8                                     |
| `leptos-fetch`                | 0.4          | Query cache for API data (0.4 is the Leptos 0.8 line)                       |
| `gloo-net`                    | 0.6          | Browser HTTP; 0.6 is what Leptos itself pulls in, so only one copy ships    |
| `leptos_ui`, `tw_merge`       | 0.3, 0.1     | Rust/UI's `clx!`/`variants!` macros and Tailwind class merging (`variant` feature) |
| `icons`                       | 0.19         | Lucide icons for Rust/UI (`features = ["leptos"]`)                          |
| Tailwind CSS                  | 4.3          | Standalone CLI, downloaded by Trunk (`[tools] tailwindcss`) or run by cargo-leptos |
| `cargo-leptos`                | 0.3          | SSR build tool, `cargo install --locked cargo-leptos`                      |
| `trunk`                       | 0.21         | CSR build tool, `cargo install --locked trunk`                             |
| `ui-cli`                      | 0.3          | Rust/UI installer (`ui init`, `ui add`); expects a cargo-leptos project     |

## Where to look

| Need                                 | Source                                                                               |
|--------------------------------------|--------------------------------------------------------------------------------------|
| Concepts, in order                   | The Leptos book: `src/` in <https://github.com/leptos-rs/book> (`src/SUMMARY.md` is the map), rendered at <https://book.leptos.dev> |
| API reference                        | <https://docs.rs/leptos>, `leptos_router`, `leptos_meta`, `leptos_axum`, `leptos-fetch` |
| Runnable examples                    | <https://github.com/leptos-rs/leptos/tree/main/examples> (`todo_app_sqlite`, `todomvc`, `counters`, `stores`, `server_fns_axum`, `ssr_modes`, `islands`) |
| SSR starter                          | `cargo leptos new --git https://github.com/leptos-rs/start-axum`                     |
| Rust/UI components, hooks, blocks    | <https://www.rust-ui.com>; source in <https://github.com/rust-ui/leptos-ui> `public/registry/styles/default/<name>.md` |
| API data caching                     | <https://github.com/zakstucke/leptos-fetch> README                                   |
| Reactive helpers for browser APIs    | [`leptos-use`](https://leptos-use.rs) (0.19)                                        |

Read the book's markdown from <https://github.com/leptos-rs/book>; a shallow clone into a scratch folder is the
fastest way to search it. Chapter paths in the references below are relative to that repository's `src/`.
Some chapters still say 0.7; the APIs they show are the same in 0.8 unless a reference here says otherwise. For
Rust/UI, clone <https://github.com/rust-ui/leptos-ui> shallowly into a scratch folder and read the registry markdown.

Read the reference that matches the task before writing code:

- [references/views.md](references/views.md): `view!` syntax, attributes, events, components and props, children and
  slots, control flow, lists with `<For/>`, forms and inputs, error boundaries, builder syntax.
- [references/reactivity.md](references/reactivity.md): signals and their access methods, derived signals, memos,
  effects, context, stores, ownership and disposal, resources, `<Suspense/>`, `<Transition/>`, actions.
- [references/routing.md](references/routing.md): `<Router/>`, `<Routes/>`, nested routes and `<Outlet/>`, params and
  queries, `<A/>`, `<Form/>`, navigation, `leptos_meta`.
- [references/fullstack.md](references/fullstack.md): CSR with Trunk, calling a JSON API (client choice, an `api.rs`
  module with RFC 9457 errors, `LocalResource` and `Action::new_local`, CORS and the Trunk proxy, contract crates),
  SSR with cargo-leptos and Axum, server functions, extractors, responses and redirects, `<ActionForm/>`, SSR modes,
  hydration bugs, islands, deployment and WASM size.
- [references/rust-ui.md](references/rust-ui.md): installing Rust/UI by CLI or by hand, the crates and macros it
  needs, which props are reactive, attribute spreading, script-driven overlays, toasts and forms, Tailwind v4 without
  Node under Trunk, the theme variables and dark mode, and gotchas from a real app.

## Start a project

Client-side only (a static site that calls an existing JSON API):

```sh
rustup target add wasm32-unknown-unknown
cargo install --locked trunk
cargo init app && cd app
cargo add leptos --features csr
cargo add console_error_panic_hook
trunk serve --open                           # http://127.0.0.1:8080, rebuilds on save
```

```html
<!-- index.html: Trunk compiles Tailwind (pin the version in Trunk.toml: [tools] tailwindcss = "4.3.3") -->
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link data-trunk rel="tailwind-css" href="tailwind.css" />
    <link data-trunk rel="rust" data-wasm-opt="z" data-cargo-profile-release="wasm-release" />
  </head>
  <body></body>
</html>
```

`tailwind.css` starts with `@import "tailwindcss";` and `@source "./src";`, then the Rust/UI theme variables
(see [references/rust-ui.md](references/rust-ui.md)). Add Rust/UI components by copying them into
`src/components/ui/`.

```rust
// src/main.rs
use leptos::prelude::*;

fn main() {
    console_error_panic_hook::set_once();
    leptos::mount::mount_to_body(App);
}

#[component]
fn App() -> impl IntoView {
    let (count, set_count) = signal(0);
    view! {
        <button on:click=move |_| *set_count.write() += 1>"Clicks: " {count}</button>
        <p>"Double: " {move || count.get() * 2}</p>
    }
}
```

Full stack (server rendering, hydration, server functions):

```sh
cargo install --locked cargo-leptos
cargo leptos new --git https://github.com/leptos-rs/start-axum
cd <name> && cargo leptos watch              # http://127.0.0.1:3000
```

The project layout and feature flags for both modes are in [references/fullstack.md](references/fullstack.md); with
cargo-leptos, `ui init` and `ui add` install Tailwind and Rust/UI ([references/rust-ui.md](references/rust-ui.md)).

## Rules that prevent most mistakes

Reactivity:

- A component body runs once. Only signals and closures in the view are reactive: `{count}` and
  `{move || count.get() * 2}` update, `{count.get()}` renders once and never changes.
- Derive state instead of syncing it. Use a closure (`let double = move || count.get() * 2`) or `Memo::new` for
  expensive work. Writing to a signal inside an `Effect` to mirror another signal is the wrong tool.
- Effects are for syncing with the outside world (DOM APIs, storage, logging). They never run on the server, so
  browser-only code belongs inside `Effect::new`.
- `.get()` clones. Use `.read()` or `.with(|v| ...)` to look without cloning, and `.write()` or `.update(|v| ...)` to
  mutate in place. Do not hold a read guard while writing the same signal; it panics at runtime.
- Signal values must be `Send + Sync`. For `web_sys` or other `!Send` values use the local variants:
  `signal_local`, `RwSignal::new_local`, `LocalResource`, `Action::new_local`.
- Signals are `Copy` handles owned by the component that created them. A signal created in a child and stored in a
  parent can be read after disposal and panic. Create collections of signals with `ArcRwSignal`, or use a store.

Views:

- Text is quoted (`<p>"Hello"</p>`), Rust goes in braces (`{value}`), and every element closes, including void
  ones (`<input/>`, `<br/>`).
- Branches returning different element types need `.into_any()` or `Either`/`EitherOf3`.
- Use `<Show when=... fallback=...>` for a branch that is costly to rebuild; a plain `move || if ...` re-renders on
  every change of its inputs.
- `<For each key children>` only re-renders a row when its key changes. Changing a field of a row does nothing
  unless the field is a signal, the row reads a memo, or the list is a `reactive_stores` store.
- Controlled inputs use `prop:value` (or `bind:value`), not `value`. The `value` attribute only sets the initial value.
- A click that navigates is an `<a>` or `<A/>`, not a button calling `use_navigate`.

Full stack:

- Everything must render the same on the server and in the browser. No `cfg!(target_arch = "wasm32")` branches in
  views, no invalid HTML (a `<div>` inside a `<p>`), and always write `<tbody>` inside `<table>`.
- Server functions are public HTTP endpoints. Authenticate and authorize inside each one, return only data the
  caller may see, and validate every argument.
- Server function types cross a 32/64-bit boundary: use `i32`/`i64`, never `usize`/`isize`. The default URL-encoded
  input handles `Option` fields and tuple enum variants poorly; switch the input encoding when they misbehave.
- Server-only crates (`sqlx`, `tokio`, `axum`) are `optional = true` and enabled only by the `ssr` feature. Errors
  about `mio` or missing `core` items mean a server crate leaked into the WASM build.
- A CSR app calling a JSON API with `gloo-net` gets `!Send` futures: load with `LocalResource`, mutate with
  `Action::new_local`, and keep every request in one `api.rs` module that turns failures into user messages.
- When several views share API data (filters, tabs, pages), cache it with leptos-fetch: one `QueryScopeLocal` per
  query, `client.local_resource(scope, keyer)`, and after a mutation write the result into every cached list with
  `client.update_query` instead of refetching.
- Ship small WASM: a `wasm-release` profile (`opt-level = "z"`, `lto = true`, `codegen-units = 1`) selected with
  Trunk's `data-cargo-profile-release` or cargo-leptos's `lib-profile-release`, and no second copy of a crate Leptos
  already brings (`cargo tree -d --target wasm32-unknown-unknown`).
- With SSR, prefer `Resource` (serialized from server to client) over `LocalResource`. Read resources under
  `<Suspense/>` or `<Transition/>`.
- Put client and server state in the URL when you can. It survives reloads, works without WASM, and the router
  makes it reactive.

Rust/UI and Tailwind:

- Rust/UI components are copied source, owned by the project. Install them with `ui add` (cargo-leptos) or by
  copying the registry markdown's code; record the registry commit in a header and change them when they fall short.
- Many props are plain values read once (`Input`'s `disabled: bool`); `Button` has no `disabled`, `type` or click
  prop. Pass what is missing with `attr:disabled=move || ...`, `attr:r#type="submit"`, `on:click=...`.
- Dialog, popover, select, dropdown and the other overlays are driven by inline `<script>`, not signals. Check a
  component for `<script` before using it in a flow that Rust must control; prefer `select_native`, `toast`
  (`show_toast()`) and plain Leptos state.
- Tailwind must see the class names: `@source "./src";` in `tailwind.css`. Keep the theme variables and the `dark:`
  variant on the same model (system media query, or a `.dark` class with a toggle).

## Working method

1. Check `Cargo.toml`: Leptos version, which of `csr`/`hydrate`/`ssr` is enabled where, `nightly`, and which
   Rust/UI components are already in `src/components/ui/`. Match the API to what is installed.
2. Find the closest book chapter or upstream example and copy its shape instead of inventing an API. For Rust/UI,
   read the component's code (it is in the project) for its props.
3. Make the change. Run `cargo check` for the target that is built: `cargo check --target wasm32-unknown-unknown`
   for CSR, `cargo check --features ssr` and `cargo check --features hydrate --target wasm32-unknown-unknown` for
   SSR projects (or just `cargo leptos build`). Macro errors point at the offending markup.
4. Format with `leptosfmt` if the project uses it (`leptosfmt.toml` at the root), then `cargo fmt`.
5. Verify in the browser with `trunk serve` or `cargo leptos watch`. Read the browser console: panics and
   hydration mismatches only show there. Add `console_error_panic_hook` so panics carry a Rust location.
6. Slow builds: `--cfg=erase_components` in development (cargo-leptos 0.2.40+ sets it automatically; for Trunk use
   `RUSTFLAGS="--cfg erase_components" trunk serve`). Never ship it in release.

Editor support: if rust-analyzer struggles with `#[server]`, add `"server"` to
`rust-analyzer.procMacro.ignored.leptos_macro`, and enable the `ssr` (or all) features so gated code is not shown
as inactive.

## Testing

- Pull logic out of components into plain types with `impl` blocks and test them with ordinary `#[test]`s.
- Component tests: `wasm-bindgen-test` with `mount_to` and `tick().await` after each interaction (see the
  `counter` example), or Playwright end-to-end tests (`cargo leptos end-to-end` runs `end2end/`).
- Server functions are plain async functions on the server and can be called from `#[tokio::test]` when their
  context (pools, settings) is provided.

## Learning path

When someone is learning rather than shipping a change, walk the book in this order and have them type each
example into a Trunk project:

1. Getting Started and Leptos DX: Trunk, `console_error_panic_hook`, rust-analyzer, `leptosfmt`.
2. Part 1, views: basic component, dynamic attributes, components and props, iteration (both chapters), forms,
   control flow, errors, parent-child communication, children and slots.
3. Reactivity: working with signals, effects, the functions interlude. Then the two appendices on the reactive graph
   and signal life cycle.
4. Async: resources, `<Suspense/>`, `<Transition/>`, actions. Then global state and stores.
5. Router: routes, nested routing, params and queries, `<A/>`, `<Form/>`. Then metadata, styling and `web_sys`.
6. Part 2: `cargo-leptos`, the life of a page load, SSR modes, hydration bugs.
7. Server functions, extractors, responses and redirects, `<ActionForm/>` and progressive enhancement.
8. Deployment, WASM binary size, and the islands guide.
9. Tailwind and Rust/UI: set up Tailwind under Trunk, copy `button`, `input`, `card` and `alert`, then restyle one of
   the earlier exercises with them and add a reactive disabled state through `attr:disabled`.

Explain each step by the concept it adds, then give a small exercise that changes the example (add a prop, turn a
derived signal into a memo, move state into the URL, replace a `spawn_local` with an action) so the idea is used,
not only read.
