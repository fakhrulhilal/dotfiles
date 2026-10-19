# CLI, features and services

Upstream guides: `docs/cli/new.md`, `docs/cli/fmt.md`, `docs/manual_setup.md`, `docs/asset.md`, `docs/tailwind.md`,
`docs/font.md`, `docs/icon.md`, `docs/ui.md`, `docs/cookie.md`, `docs/session.md`, `docs/mail.md`, `docs/htmx.md`,
`docs/alpine_ajax.md`, `docs/datastar.md` and `docs/router/content/`. Each service has an `examples/<name>` app.

Every bullet here is a summary. Open the matching guide before using a service for the first time.

## CLI

| Command                                            | Purpose                                                         |
|----------------------------------------------------|-----------------------------------------------------------------|
| `topcoat new <dir> --recommended` (or `--minimal`) | create an app; no preset runs the wizard                        |
| `topcoat dev`                                      | build, bundle assets, serve and reload on change (`r` rebuilds) |
| `topcoat fmt [--check] [--rustfmt]`                | format macro bodies; `--stdin --rustfmt` for editor integration |
| `topcoat asset bundle --release`                   | build the asset bundle by hand; must match profile and package  |
| `topcoat ui init` / `add` / `list` / `remove`      | manage copied UI components                                     |

`topcoat fmt --rustfmt` pipes source to rustfmt, so set `edition` in `rustfmt.toml`.

## Manual setup

```sh
cargo new app && cd app
cargo add topcoat
cargo add tokio --features rt-multi-thread,macros
```

`topcoat::start(router)` binds `127.0.0.1:3000` unless `HOST`/`PORT` are set. `serve(listener, router)` and
`Router::handle(request)` support custom hosting.

## Features

Default: router, view, runtime, serve, discover, asset, compression, cookie, session, font, icon. Opt in to the rest
by name, or use `full`. Exact flags are in `crates/topcoat/Cargo.toml`.

A feature that runs at build time is needed on both dependency tables:

```toml
[dependencies]
topcoat = { version = "0.10", features = ["tailwind", "font-fontsource"] }

[build-dependencies]
topcoat = { version = "0.10", default-features = false, features = ["tailwind"] }
```

## Assets

- `asset!("./image.png")` renders a hashed URL. `./` and `../` resolve from the source file, bare paths from the crate
  root, and HTTP(S) URLs are downloaded and cached.
- `.assets(AssetBundle::load()?)` loads the `assets/` directory beside the binary. A custom `--out` needs `load_dir`.
- Deploy the binary together with its matching bundle. A missing asset panics.
- `AssetConfig::hosted_at(base, catalog)` serves files uploaded separately to a CDN.

## Tailwind

```rust
// build.rs
fn main() {
    topcoat::tailwind::BuildConfig::new().input("styles.css").render().unwrap();
}
```

```rust
<link rel="stylesheet" href=(topcoat::tailwind::stylesheet!())>
```

Needs the asset bundle. The scanner finds literal class names only, so never assemble a class name from pieces
(`format!("text-{color}-500")` is not found). List whole names in `class!`.

## Fonts and icons

- `font!("Family", @font-face { src: url(...); ... })` declares a font; `font_face!` declares one face. Register with
  `.discover()` or `.font(FONT)` and load with `font::link(font: FONT)`.
- With `font-fontsource`: `const GEIST: Font = fontsource_font!(GEIST, weight: [400, 700], host: Asset);` bundles the
  files. The default host is the CDN.
- `icon(data: ICON, label: "Meaning")` renders an SVG. Without a label the icon is decorative.
- With `icon-iconify` on both dependency tables, `build.rs` stages a set with
  `topcoat::icon::iconify::BuildConfig::new().icon_set("feather").stage().unwrap()`. Then
  `iconify::include!("feather")` creates `feather::TRASH_2`, and `iconify_icon!("feather:trash-2")` yields one icon.

## UI components

`topcoat ui init`, then `topcoat ui add button`. Components are copied into `src/components` and are yours to edit.
Commit `components.toml`. Compile the installed `styles.css` with Tailwind and load its font. Props follow one shape:
variant and size enums, `attrs`, then children.

```rust
button(variant: ButtonVariant::Outline, size: ButtonSize::Sm, attrs: attributes! { type="submit" }, "Save")
```

`ui list` shows updates, `add --overwrite` replaces local edits and `remove` deletes. A `.dark` ancestor selects the
dark theme.

## Cookies

```rust
use topcoat::cookie::{Cookies, cookie, cookies, time::Duration};

cookies(cx).get("customer");
cookies(cx).add(cookie! {
    "customer" = name.to_owned();
    Path = "/";
    HttpOnly;
    SameSite = Lax;
    MaxAge = Duration::days(30)
});
cookies(cx).remove(cookie!("customer" = ""; Path = "/"));
```

- Enable with `.cookies()` (`RouterBuilderCookieExt`) and import the `Cookies` trait.
- Write before the response seals. A late write panics. Errors and redirects keep pending writes.
- Removal needs the same name, path and domain.
- Signed cookies are authenticated; private cookies are encrypted and authenticated. Both need a persisted `Key` in
  app context.
- `cookie_store::<T, _>(jar, "name").parse_or_default().update(...).commit()?` writes JSON. Without `commit` the change
  is discarded.

## Sessions

`.cookies().sessions(SessionConfig::default())`. Topcoat issues and transports the token; the application owns
storage.

- After authenticating, call `session::start(cx).await?` and store the token hash, user and expiry. Never store the
  raw token.
- `token_hash(cx).await?` only identifies the session. The application must look it up and check expiry.
- `stop` returns the hash to delete. `refresh` requires validation and a persisted expiry. `rotate` requires revoking
  the old hash and persisting the replacement.
- The default cookie is `__Host-` prefixed, `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`. Mutate before streaming.

## Mail

Features `mail` and `mail-smtp`. Register `.mail(MailConfig::builder().transport(...).build())`, build with
`mail! { from: "...", to: "...", html: { cx => ... } }?` and deliver with `send(cx, mail).await?`. Plain text is derived
from the HTML. `FileTransport` writes `.eml`, `MemoryTransport` is for tests, `SmtpTransport` delivers.

## Optional HTTP integrations

| Feature       | Use                                                                                             |
|---------------|-------------------------------------------------------------------------------------------------|
| `fs`          | `.public_dir("./public")` or `.serve_dir("/files/{*rest}", dir)` (`RouterBuilderDirectoryExt`)  |
| `sse`         | `Sse::new(stream)` of `Result<Event>`, `.keep_alive(KeepAlive::new())`, `last_event_id(cx)`     |
| `websocket`   | GET handler takes `WebSocketUpgrade`; authenticate before `on_upgrade`                          |
| `multipart`   | `next_field().await?`; consume or drop each field before the next; `BodyLimit` spans all fields |
| `sitemap`     | return `Sitemap::new().url(...)`; relative entries need `.base_url(...)`                        |
| `tower`       | `TowerRoute` mounts a service, `TowerLayer` wraps handling, `TowerService` exposes Topcoat      |
| `htmx`        | `hx_request(cx)` picks a fragment; `Hx*` response parts go before the body                      |
| `alpine-ajax` | `ajax_request` / `ajax_targets` pick fragments; load the plugin before Alpine, both deferred    |
| `datastar`    | `Signals<T>` reads GET query or non-GET body; `PatchElements` / `PatchSignals` emit SSE updates |

htmx, Alpine AJAX and Datastar are alternatives to the Topcoat runtime. Load each library's browser script yourself.
Streams must own their captures or clone `Cx`; add `+ use<>` to the return type to avoid borrowing the context.

## Data access

Topcoat has no database layer. The generated app and `examples/toasty-todo` use Toasty with SQLite: build the `Db` in
`main`, pass it to `.app_context(db)` and read it through a helper such as `fn db(cx: &Cx) -> Db`. Any other client
works the same way.