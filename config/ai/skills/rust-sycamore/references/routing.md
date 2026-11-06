# Routing with sycamore-router

Book chapter: `router.md`. Example: `examples/router`. Source: `packages/sycamore-router/src/router.rs` (small;
read it when behaviour surprises you). `sycamore-router = "0.9.4"` with `sycamore = "0.9.4"`.

## Routes

```rust
use sycamore_router::{HistoryIntegration, Route, Router};

#[derive(Route, Clone)]
enum AppRoute {
    #[to("/")]
    Home,
    #[to("/hello/<name>")]
    Hello { name: String },
    #[to("/account/<id>")]
    Account { id: u32 },              // any FromStr type; /account/abc does not match
    #[to("/page/<path..>")]
    Page { path: Vec<String> },       // zero or more segments, matched lazily
    #[to("/unit/<n>")]
    Unit(u32),                        // tuple variants work too
    #[to("/admin/<_..>")]
    Admin(AdminRoute),                // nested route enum, matched on the rest of the path
    #[not_found]
    NotFound,                         // exactly one, required
}
```

Only the path is matched. The query string and hash are not part of a route.

## The router

```rust
#[component]
fn App() -> View {
    view! {
        Router(
            integration=HistoryIntegration::new(),
            view=|route: ReadSignal<AppRoute>| view! {
                header { a(href="/") { "Home" } }
                main {
                    match route.get_clone() {
                        AppRoute::Home => HomePage(),
                        AppRoute::Hello { name } => { h1 { "Hello, " (name) } },
                        AppRoute::NotFound => NotFound(),
                        _ => {},
                    }
                }
            },
        )
    }
}
```

- Only one `Router` may exist at a time (it asserts).
- Navigation is by plain `a(href=...)`. The router's click handler is attached to the **top-level nodes the
  `view` closure returns** (`header` and `main` above) and catches clicks on links inside them. Return static
  top-level elements around the dynamic part. The handler is attached once, on mount, to the nodes there at
  that moment, and the source marks it "TODO: this does not work for dynamic views": a router view whose top
  level is itself a `match` risks losing the handler when those nodes are replaced, and links then reload the
  page.
- Ctrl/Cmd/Shift/Alt-clicks and links to another origin are left to the browser.
- `a(href=..., rel="external")` opts out: use it for anything the server serves outside the app (an API
  reference, a file, another app under the same host).
- `navigate("/path")` pushes a new entry, `navigate_replace` replaces the current one, `refresh()` re-renders the
  current route. They panic outside a `Router`.
- A `<base href="/sub/">` in `index.html` is honoured: paths are matched with that prefix stripped.

## Query strings

```rust
use sycamore_router::{use_search_query, use_search_queries};

let q: ReadSignal<Option<String>> = use_search_query("q");       // ?q=...
let all: ReadSignal<HashMap<String, String>> = use_search_queries();
```

Links that change only the query (`a(href="/?filter=active")`) are handled without a reload, and these signals
update.

**Gap in 0.9.4:** Back and Forward between two addresses with the same path and a different query do not update
them. The router's `popstate` handler only compares the path, so the URL changes and the page does not. If the
query holds page state (filters, tabs, search), listen to `popstate` too and read the address after either:

```rust
use sycamore::web::js_sys;
use sycamore::web::wasm_bindgen::{prelude::Closure, JsCast, UnwrapThrowExt};
use web_sys::UrlSearchParams;            // web-sys features: "Location", "UrlSearchParams"

fn use_filter() -> ReadSignal<StatusFilter> {
    let followed_link = use_search_query("filter");
    let went_back = create_signal(());

    let on_popstate = Closure::<dyn Fn()>::new(move || went_back.set(()));
    let listener = on_popstate.as_ref().unchecked_ref::<js_sys::Function>().clone();
    window().add_event_listener_with_callback("popstate", &listener).unwrap_throw();
    on_cleanup(move || {
        let _ = window().remove_event_listener_with_callback("popstate", &listener);
        drop(on_popstate);
    });

    // A selector: dependents (the list load) run only when the filter really changed.
    create_selector(move || {
        followed_link.track();
        went_back.track();
        let search = window().location().search().unwrap_or_default();
        UrlSearchParams::new_with_str(&search)
            .ok()
            .and_then(|params| params.get("filter"))
            .and_then(|value| value.parse().ok())
            .unwrap_or_default()
    })
}
```

Call it inside a component under the `Router` (it uses the router's state). Test it: choose two filters, press
Back, and check the list and the "current" marker follow.

Mark the current link with `aria-current`, an optional attribute: `aria-current=move || (current.get() ==
option).then_some("page")`.

## Hash

`use_location_hash()` returns a `ReadSignal<String>` that follows `hashchange`. Links that change only the hash are
left to the browser.

## Server side: `StaticRouter`

```rust
// `match_path` takes `&self` (the book's `AppRoutes::match_path(path)` does not compile).
let route = AppRoute::default().match_path(path);
view! { StaticRouter(route=route, view=|route: ReadSignal<AppRoute>| view! { /* same view */ }) }
```

Use it when rendering with `render_to_string`; the browser side hydrates with `Router` and `HistoryIntegration`.

## Hosting a router app

A static host must answer unknown paths with `index.html` so deep links and reloads work: Caddy
`try_files {path} /index.html`, nginx `try_files $uri /index.html`, GitHub Pages a copy of `index.html` as
`404.html`. Keep filters in the query string of `/` and no fallback is needed for them at all.
