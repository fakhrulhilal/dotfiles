# Async, resources and calling a JSON API

Book chapter: `guide/resources-and-suspense.md`. Examples: `http-request`, `ssr-suspense`,
`transitions`. Source: `packages/sycamore-futures/src/` and `packages/sycamore-web/src/{resource,suspense}.rs`.
All of it needs the `suspense` feature: `sycamore = { version = "0.9.4", features = ["suspense"] }`.

## Tasks

```rust
use sycamore::futures::{spawn_local, spawn_local_scoped};

spawn_local_scoped(async move { /* aborted when the current scope is disposed */ });
spawn_local(async move { /* runs to the end, whatever happens to the scope */ });
```

Use `spawn_local_scoped` from components, rows and effects. It is aborted when its scope goes: when a row is
removed, a branch switches, or an effect re-runs. It is aborted at its next `.await`, not immediately: code after
the last `.await` still runs within the same poll, even if that code disposes the scope (see
[pitfalls.md](pitfalls.md#signal-was-disposed)).

## Resources

```rust
use sycamore::web::{create_client_resource, create_isomorphic_resource, Suspense, Transition};

let user = create_client_resource(on(id, move || async move { api::user(id.get()).await }));

view! {
    Suspense(fallback=|| view! { p { "Loading..." } }) {
        if let Some(Ok(user)) = user.get_clone() {
            p { (user.name) }
        }
    }
}
```

- A `Resource<T>` derefs to `ReadSignal<Option<T>>`: `None` until the first load, then the last value, which it
  keeps while a refetch runs. `.is_loading()` says whether one is running.
- `create_client_resource` never fetches during SSR; `create_isomorphic_resource` fetches on both sides.
- Async code is not tracked: name the dependencies with `on(deps, ...)` (a signal, or a tuple of them).
- There is no `refetch()` and no way to write into a resource. To retry, bump a signal listed in `on(...)`. When
  the page changes the data itself (add, edit, delete), keep the list in a plain `Signal<Vec<T>>` that the page
  owns and load it in an effect instead (below).
- `Suspense` shows `fallback` while any resource read beneath it is loading. `Transition` shows the fallback only
  the first time and keeps the old view on screen during later loads.
- Both take `set_is_loading=move |loading| busy.set(loading)` to show a spinner of your own while a `Transition`
  keeps the old view.

## Async components

```rust
#[component]
async fn Visits() -> View {
    let visits = api::visits().await.unwrap_or_default();
    view! { p { "Total visits: " (visits.value) } }
}

view! { Suspense(fallback=|| "Loading...".into()) { Visits {} } }
```

An async component must sit under a `Suspense`. `on_mount` inside one runs after its first `.await`.

## A page that loads and changes a list

The shape that works for a list the user edits (from a todo client built on 0.9.4):

```rust
/// The list on screen and what the page does to it. All signals, so `Copy`.
#[derive(Clone, Copy)]
struct Todos {
    items: Signal<Vec<Todo>>,
    error: Signal<String>,
    filter: ReadSignal<StatusFilter>,
}

impl Todos {
    /// Awaits an API call, showing its error if it fails.
    async fn run<T>(self, call: impl Future<Output = Result<T, String>>) -> Option<T> {
        self.error.set(String::new());
        match call.await {
            Ok(value) => Some(value),
            Err(message) => { self.error.set(message); None }
        }
    }

    /// Writes a created or updated item in, or takes it out if the filter no longer selects it.
    fn place(self, todo: Todo) {
        let shown = self.filter.get_untracked().matches(&todo);
        self.items.update(|items| match (items.iter().position(|i| i.id == todo.id), shown) {
            (Some(i), true) => items[i] = todo,
            (Some(i), false) => drop(items.remove(i)),
            (None, true) => items.push(todo),
            (None, false) => {}
        });
    }
}

#[derive(Clone, PartialEq)]
enum Load { Loading, Loaded, Failed(String) }

// In the page component:
let load = create_signal(Load::Loading);
let reload = create_signal(());                      // Retry sets it
create_effect(move || {
    let filter = filter.get();                       // tracked, synchronously
    reload.track();
    spawn_local_scoped(async move {                  // aborted if the effect runs again first
        match api::list(filter).await {
            Ok(list) => batch(|| { todos.items.set(list); load.set(Load::Loaded); }),
            Err(message) => load.set(Load::Failed(message)),
        }
    });
});
```

And in each row, update the row's own state *before* the call that may remove or replace the row:

```rust
let on_delete = move |_| {
    busy.set(true);
    spawn_local_scoped(async move {
        let deleted = todos.run(api::delete(id)).await.is_some();
        busy.set(false);                 // the row's signal, while the row still exists
        if deleted {
            todos.remove(id);            // last: this disposes the row and `busy`
        }
    });
};
```

## The API module

Keep every HTTP call in one module that returns messages ready to show. With gloo-net 0.7:

```rust
use gloo_net::http::{Request, Response};

/// Fixed at build time: `TODO_API_URL=https://api.example.com trunk build --release`.
/// Empty means the address the site is served from.
const API_URL: &str = match option_env!("TODO_API_URL") {
    Some(url) => url,
    None => "http://127.0.0.1:8080",
};

pub async fn create(input: CreateTodo) -> Result<Todo, String> {
    let request = Request::post(&url("/api/v1/todos")).json(&input);
    json(send(request).await?).await
}

async fn send(request: Result<Request, gloo_net::Error>) -> Result<Response, String> {
    let response = match request { Ok(r) => r.send().await, Err(e) => Err(e) }
        .map_err(|_| "The service is unreachable.".to_owned())?;
    if response.ok() { Ok(response) } else { Err(problem_message(response).await) }
}

/// RFC 9457: `errors` (member -> messages) first, then `detail`, then `title`.
#[derive(Default, serde::Deserialize)]
struct Problem {
    title: Option<String>,
    detail: Option<String>,
    #[serde(default)]
    errors: std::collections::BTreeMap<String, Vec<String>>,
}
```

- gloo-net futures are `!Send`, which is fine: Sycamore's tasks are local.
- The browser logs a console error for every non-2xx response; that is not a bug in the app.
- Share request and response types with the server through a contract crate (`serde` types, no server deps).
- `Request::json` returns `Result<Request, _>`; `Request::get(..).build()` does too.

## DOM access and browser events

```rust
use sycamore::web::wasm_bindgen::{prelude::Closure, JsCast, UnwrapThrowExt};
use sycamore::web::js_sys;

let node = create_node_ref();
on_mount(move || {
    let input: web_sys::HtmlInputElement = node.get().unchecked_into();
    let _ = input.focus();
    input.select();
});

// A window event listener, removed with the scope.
let fired = create_signal(());
let callback = Closure::<dyn Fn()>::new(move || fired.set(()));
let listener = callback.as_ref().unchecked_ref::<js_sys::Function>().clone();
window().add_event_listener_with_callback("popstate", &listener).unwrap_throw();
on_cleanup(move || {
    let _ = window().remove_event_listener_with_callback("popstate", &listener);
    drop(callback);
});
```

`window()` and `document()` are in the prelude. Each `web_sys` type used by name needs its feature in
`Cargo.toml` (`web-sys = { version = "0.3", features = ["HtmlInputElement", "KeyboardEvent", ...] }`).
Never `Closure::forget()` a listener that captures signals: after the scope is gone it would touch disposed
signals and panic.
