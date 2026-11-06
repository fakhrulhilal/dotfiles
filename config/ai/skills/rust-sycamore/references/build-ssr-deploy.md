# Build, SSR, deployment and testing

Book chapters: `introduction/your-first-app.md`, `cookbook/styling.md`, `cookbook/optimize-wasm-size.md`,
`server-side-rendering.md`, `server-side-rendering/streaming.md`. Examples: `hydrate`, `ssr`, `ssr-streaming`,
`ssr-suspense`.

## Trunk

```toml
# Trunk.toml, next to index.html
[build]
target = "index.html"
dist = "dist"

[serve]
addresses = ["127.0.0.1"]
port = 8086
```

```html
<link data-trunk rel="css" href="assets/app.css" />
<link data-trunk rel="rust" data-bin="my-app" data-wasm-opt="z" data-cargo-profile-release="wasm-release" />
```

- `trunk serve` rebuilds on save; `trunk build --release` writes the static site to `dist/`.
- In a Cargo workspace, run Trunk from the crate's folder (where `Trunk.toml` is); `data-bin` names the binary.
- Trunk downloads `wasm-bindgen` (matching `Cargo.lock`) and `wasm-opt` on the first build.
- A plain `cargo build` builds for the host. Give the target explicitly for checks:
  `cargo clippy --target wasm32-unknown-unknown -p my-app`. Exclude the crate from a workspace's
  `default-members` so `cargo test` on the host skips it.

## Styling

Sycamore has no styling system. Plain CSS through Trunk (`rel="css"`), a CSS framework, or Tailwind (Trunk's
`rel="tailwind-css"` with `[tools] tailwindcss = "4.x"` in `Trunk.toml`, no Node needed). Style by attributes as
well as classes: `[aria-current="page"]`, `:checked`, `:disabled` follow state without extra code. For a native
checkbox styled as a circle, `appearance: none` plus `:checked::before { content: "\2713" }` works in current
browsers.

## WASM size

```toml
# root Cargo.toml
[profile.wasm-release]
inherits = "release"
opt-level = "z"
lto = true
codegen-units = 1
panic = "abort"
```

Select it with `data-cargo-profile-release="wasm-release"` and run `wasm-opt` with `data-wasm-opt="z"`. A todo
client (router, gloo-net, serde, uuid, chrono) came to about 220 KB of WASM, 95 KB gzipped. `cargo tree -d --target
wasm32-unknown-unknown` finds crates bundled twice.

## Static hosting and Docker

The output is static files. The host must serve `index.html` for unknown paths (see
[routing.md](routing.md#hosting-a-router-app)). The API address is compiled in (`option_env!`), so build once per
environment, or build with an empty value and serve the API from the same origin through the host's reverse proxy,
which also avoids CORS.

This is the image of todo-app's `frontend/sycamore` (build context: the workspace root):

```dockerfile
# syntax=docker/dockerfile:1
ARG RUST_VERSION=1.98
ARG TRUNK_VERSION=0.21.14

FROM --platform=$BUILDPLATFORM rust:${RUST_VERSION}-trixie AS build
ARG TRUNK_VERSION
RUN rustup target add wasm32-unknown-unknown \
    && curl -L --proto '=https' --tlsv1.2 -sSf \
       https://raw.githubusercontent.com/cargo-bins/cargo-binstall/main/install-from-binstall-release.sh | bash \
    && cargo binstall --no-confirm "trunk@${TRUNK_VERSION}"

WORKDIR /src
COPY Cargo.toml Cargo.lock ./
COPY contract contract
COPY frontend/sycamore frontend/sycamore
# The workspace lists every implementation; here it lists only what this image
# builds. Cargo.lock still pins every version.
RUN sed -i -E 's#^(default-)?members = .*#\1members = ["contract", "frontend/sycamore"]#' Cargo.toml

# Empty: call the API on the address the site is served from (the proxy).
ARG TODO_API_URL=""
WORKDIR /src/frontend/sycamore
RUN --mount=type=cache,target=/usr/local/cargo/registry,sharing=locked \
    --mount=type=cache,target=/usr/local/cargo/git,sharing=locked \
    --mount=type=cache,target=/src/target,id=todo-sycamore-web-target,sharing=locked \
    --mount=type=cache,target=/root/.cache/trunk,id=todo-sycamore-trunk,sharing=locked \
    trunk build --release --dist /out

FROM caddy:2-alpine
COPY --from=build /out /srv
```

Caddy's configuration comes from compose: `/api/*` and `/health/*` go to the API, everything else is
`try_files {path} /index.html` and `file_server`.

The site is the same for every platform, so build it once on `$BUILDPLATFORM`; only the server image is
per-architecture.

## Server-side rendering

```rust
let html = sycamore::render_to_string(App);                         // sync: suspense fallbacks only
let html = sycamore::render_to_string_await_suspense(App).await;    // blocking: waits for all suspense
let stream = sycamore::render_to_string_stream(App);                // streaming: fallback first, then chunks
```

- The last two need the `suspense` feature; hydrating in the browser needs `hydrate`.
- Browser side: `sycamore::hydrate(App)` (or `hydrate_to(App, &node)`) instead of `render`. Include
  `sycamore::web::HydrationScript {}` in the rendered page's `head`.
- One crate can build both sides. `is_ssr!()` / `is_not_ssr!()` are booleans and block macros; `#[cfg_ssr]` and
  `#[cfg_not_ssr]` gate items. Target-gate server dependencies:
  `[target.'cfg(any(not(target_arch = "wasm32"), sycamore_force_ssr))'.dependencies]`.
- `NoSsr { ... }` renders only in the browser (charts, things that need `window`); `NoHydrate { ... }` is
  rendered on the server and left alone by hydration (static content, script tags).
- Server and client must render the same tree. Keep browser-only work (`window()`, `NodeRef::get()`, storage) in
  `on_mount`, event handlers or `NoSsr`.
- Sycamore's tasks are `!Send`. On an axum server, render inside a `tokio::task::LocalSet` (see
  `examples/ssr-streaming/src/server.rs`, which runs it on a blocking thread with `Handle::block_on`).
- Routing on the server: `StaticRouter` with `AppRoute::default().match_path(path)`.
- For a framework with SSG and i18n, the book points to Perseus, which still targets Sycamore 0.8.

## Testing

- Unit tests: plain functions and types, ordinary `#[test]`. Reactive logic inside `create_root(|| ...)` runs
  natively.
- Static rendering: `render_to_string(App)` on the host and assert on the HTML.
- Component tests in a browser: `wasm-bindgen-test` with `sycamore::render_to(App, &container)`; there is no
  official helper, and the book's testing page is a stub (issue #55).
- End to end: Playwright against the built site, elements located by role and label. This is where disposal
  panics, the router's Back behaviour and focus show up; read the console in each failing trace.
