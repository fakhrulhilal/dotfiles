# Routing and metadata

Book chapters (`src/` of <https://github.com/leptos-rs/book>): `router/16_routes.md` to `router/20_form.md`, `metadata.md`, `interlude_styling.md`,
`deployment/sub_path.md`. Docs: <https://docs.rs/leptos_router>, <https://docs.rs/leptos_meta>.

`leptos_router` is a separate crate. It has no `csr`/`hydrate` features, only `ssr`, which the server build enables.

## Defining routes

```rust
use leptos::prelude::*;
use leptos_router::{
    components::{A, Outlet, ParentRoute, ProtectedRoute, Redirect, Route, Router, Routes},
    path,
};

#[component]
pub fn App() -> impl IntoView {
    view! {
        <Router>
            <nav>
                <A href="/">"Home"</A>
                <A href="/contacts">"Contacts"</A>
            </nav>
            <main>
                <Routes fallback=|| "Not found.">
                    <Route path=path!("/") view=Home/>
                    <ParentRoute path=path!("/contacts") view=ContactList>
                        <ParentRoute path=path!(":id") view=ContactInfo>
                            <Route path=path!("") view=EmailAndPhone/>
                            <Route path=path!("address") view=Address/>
                        </ParentRoute>
                        <Route path=path!("") view=|| view! { <p>"Select a contact."</p> }/>
                    </ParentRoute>
                    <ProtectedRoute
                        path=path!("/admin")
                        view=Admin
                        condition=move || user.get().map(|u| u.is_admin)   // Option<bool>; None = still loading
                        redirect_path=|| "/login"
                    />
                    <Route path=path!("/old") view=|| view! { <Redirect path="/"/> }/>
                    <Route path=path!("/*any") view=NotFound/>
                </Routes>
            </main>
        </Router>
    }
}
```

- One `<Router/>` per app, near the root. Everything outside `<Routes/>` shows on every page.
- `path!("/users/:id")` builds the segments: static parts, `:param`, `:param?` (optional) and `*wildcard`. The
  segment types (`StaticSegment("")`, `ParamSegment("id")`, `WildcardSegment("any")`) are the macro-free form.
- `view=Home` works for a component without props; otherwise `view=|| view! { <Home id=1/> }`.
- `<Router base="/my-app">` when the app is served under a sub path.
- `<FlatRoutes/>` is a cheaper alternative when no route is nested.

## Nested routes are layout

A nested route renders the parent and the child at the same time. The parent decides where the child goes with
`<Outlet/>`; without it the child never shows. Add a `path!("")` child so the parent URL itself matches.

Navigating between `/contacts/1` and `/contacts/2` keeps `<ContactList/>` mounted (search boxes keep their state) and
only updates what reads the `id` param.

Split route trees into components:

```rust
#[component(transparent)]
fn ContactRoutes() -> impl MatchNestedRoutes + Clone {
    view! {
        <ParentRoute path=path!(":id") view=ContactInfo>
            <Route path=path!("") view=EmailAndPhone/>
        </ParentRoute>
    }
    .into_inner()
    .into_any_nested_route()
}
```

## Params and queries

```rust
use leptos::Params;
use leptos_router::{hooks::{use_params, use_params_map, use_query, use_query_map, query_signal}, params::Params};

#[derive(Params, PartialEq)]
struct ContactParams { id: Option<u32> }       // fields must be Option<T> on stable

let params = use_params::<ContactParams>();    // Memo<Result<ContactParams, ParamsError>>
let id = move || params.read().as_ref().ok().and_then(|p| p.id);

let query = use_query_map();                   // Memo<ParamsMap>
let search = move || query.read().get("q").unwrap_or_default();

// read and write one query parameter, e.g. a list filter kept in the URL
let (filter, set_filter) = query_signal::<String>("filter");
set_filter.set(Some("active".into()));         // None removes it
```

Params come from the route that defines them and its children; the query is visible everywhere. Treat both as user
input: handle the `Err` and `None` cases.

Other hooks: `use_location()` (pathname, search, hash, state as signals), `use_url()`, `use_navigate()`,
`use_matched()`.

## Links and navigation

- Plain `<a href>` already navigates client-side. The router skips links with `target`, `download`,
  `rel="external"`, another origin, a modifier key, or a prevented default.
- `<A href="...">` also resolves relative paths against the current nested route and sets `aria-current="page"` on
  the active link (style it with `[aria-current="page"]`). Props: `exact`, `strict_trailing_slash`, `scroll`,
  `target`.
- Programmatic: `let navigate = use_navigate(); navigate("/done", NavigateOptions { replace: true, ..Default::default() });`.
  Use it after an async result, not as a click handler on a button.
- `<Router set_is_routing=setter>` reports navigation in progress (for a loading bar). `<RoutingProgress/>` is a
  built-in progress bar.

## Forms that navigate

```rust
use leptos_router::components::Form;

let query = use_query_map();
let search = move || query.read().get("q").unwrap_or_default();
let results = Resource::new(search, |q| fetch_results(q));

view! {
    <Form method="GET" action="">
        <input type="search" name="q" value=search oninput="this.form.requestSubmit()"/>
    </Form>
    <Transition fallback=|| ()>{move || results.get().map(|r| view! { <Results r/> })}</Transition>
}
```

`<Form method="GET">` updates the URL query without a reload; the resource follows the URL. It also works without
WASM. `method="POST"` posts and handles the response. `<ActionForm/>` (see fullstack.md) builds on it for server
functions.

## Metadata (`leptos_meta`)

```rust
use leptos_meta::{provide_meta_context, Body, Html, Link, Meta, Script, Style, Stylesheet, Title};

#[component]
pub fn App() -> impl IntoView {
    provide_meta_context();                    // once, at the root
    view! {
        <Stylesheet id="leptos" href="/pkg/app.css"/>
        <Title formatter=|text| format!("{text} | Todos")/>
        <Html {..} lang="en" data-theme="dark"/>
        <Router>/* ... */</Router>
    }
}

#[component]
fn Page() -> impl IntoView {
    view! { <Title text="Inbox"/> <Meta name="description" content="Your todos"/> }
}
```

- Use these components inside the body of components, never inside a server-rendered `<head>`; the shell uses
  `<MetaTags/>` to output what they collected.
- `<Script/>` goes into `<head>`; a lowercase `<script>` stays where it is written.
- For a title or meta that depends on async data during SSR, use a blocking resource or `SsrMode::Async` on the route
  (see fullstack.md).

## Styling

Leptos has no CSS opinion:

- Trunk: `<link data-trunk rel="css" href="style.css"/>` in `index.html`.
- cargo-leptos: `style-file` (CSS or SCSS) is built to `/pkg/<output-name>.css`; files in `public/` are served from
  `/`. Tailwind is built in through `tailwind-input-file`.
- Scoped CSS crates: `stylers` (compile time), `stylance` (CSS modules in files), `styled` (runtime).
- Tailwind CSS v4 with Rust/UI components is the house style; see rust-ui.md for the setup under Trunk and
  cargo-leptos.
