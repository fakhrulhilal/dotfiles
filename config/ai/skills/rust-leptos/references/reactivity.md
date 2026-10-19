# Reactivity, state and async

Book chapters (`src/` of <https://github.com/leptos-rs/book>): `reactivity/`, `async/`, `15_global_state.md`, `view/04b_iteration.md` (stores),
`appendix_reactive_graph.md`, `appendix_life_cycle.md`, `server/28_async_quick_reference.md`.

## Signals

```rust
let (count, set_count) = signal(0);          // ReadSignal<i32>, WriteSignal<i32>
let name = RwSignal::new(String::new());     // one read-write handle, handy in structs and closures
let shared = ArcRwSignal::new(Vec::new());   // reference counted, not tied to the owner tree
```

| Read                  | Behaviour                                               |
|-----------------------|---------------------------------------------------------|
| `.get()`              | Clone the value and track it                            |
| `.read()`             | Read guard, no clone, tracked (`names.read().len()`)    |
| `.with(\|v\| ...)`    | Borrow inside a closure, tracked                        |
| `.get_untracked()`, `.with_untracked(...)` | Read without subscribing            |
| `.try_get()`          | `None` if the signal was disposed                        |

| Write                 | Behaviour                                               |
|-----------------------|---------------------------------------------------------|
| `.set(v)`             | Replace and notify                                      |
| `.write()`            | Write guard, mutate in place, notifies on drop          |
| `.update(\|v\| ...)`  | Mutate inside a closure and notify                      |
| `.try_update(...)`    | Same, returns the closure's result                      |
| `.set_untracked(v)`, `.update_untracked(...)` | Change without notifying         |

Holding a read guard while writing the same signal (or the reverse) panics. Keep guards short or use
`with`/`update`.

Signals notify on every write, even when the value is equal. Memos only notify when the new value differs.

On nightly with the `nightly` feature, `count()` means `count.get()` and `set_count(v)` means `set_count.set(v)`.

### Deriving values

| Need                                | Use                                                    |
|-------------------------------------|--------------------------------------------------------|
| B is cheap to compute from A        | Derived signal: `let b = move \|\| a.get() * 2;`       |
| B is expensive, or read in many places | `let b = Memo::new(move \|_\| expensive(a.get()));` |
| A and B change together             | Two signals, set both in the same handler              |
| Map a field of a signal             | `Signal::derive`, a memo, or a store field              |
| Store a non-reactive value with a `Copy` handle | `StoredValue::new(v)`, `.read_value()`, `.update_value()` |

Avoid an `Effect` that copies A into B. It runs twice per change, risks loops, and hides the data flow.

`Signal<T>` is the type-erased "anything readable" wrapper (signal, memo, derived closure, or plain value). Take it
in component props with `#[prop(into)]`.

## Effects

```rust
Effect::new(move |_| {
    // runs after render, then again whenever a signal read here changes
    log!("value is {}", value.get());
});

let stop = Effect::watch(
    move || num.get(),                          // tracked
    move |num, prev, _| log!("{num} after {prev:?}"),  // untracked
    false,                                      // run immediately?
);
stop.stop();
```

- Dependencies are tracked automatically and per run: only signals read in the branch taken this time count.
- Effects run a tick after the component, so `NodeRef`s are filled by then.
- Effects do not run during server rendering. `Effect::new_isomorphic` runs on both.
- `on_cleanup(|| ...)` runs when the current owner is disposed (the "unmount" hook).
- Before writing an effect, check `leptos-use` for a ready-made reactive wrapper (storage, intervals, media queries,
  element size, websockets...).

## Ownership and disposal

Arena signals (`signal`, `RwSignal`, `Memo`, `StoredValue`) are `Copy` indexes owned by the reactive owner that
created them, usually the component or the branch of a `<Show/>`. When that owner is cleaned up the value is freed:

- A signal created deep in the tree and stored higher up can be read after disposal and panic. Hoist creation to
  the parent, or use the `Arc*` variant.
- Signals created higher up and removed from a list stay alive until their owner dies. Dispose them manually
  (`.dispose()`) or use `ArcRwSignal` for per-row state.

## Context

```rust
#[derive(Clone, Copy)]
struct CurrentUser(RwSignal<Option<User>>);

provide_context(CurrentUser(RwSignal::new(None)));       // in an ancestor
let user = expect_context::<CurrentUser>();              // in any descendant; use_context returns Option
```

Context follows the view tree and is keyed by type. Only the places that read the signal update, never the
components in between.

## Global state

In order of preference: the URL (route params and query, see routing.md), signals in context, a store in context.
Most state should stay local to the component that owns it.

## Stores (`reactive_stores`)

```rust
use reactive_stores::{Field, Store, StoreFieldIterator};

#[derive(Store, Clone, Debug, Default)]
struct Todos {
    filter: Filter,
    #[store(key: u32 = |todo| todo.id)]
    items: Vec<Todo>,
}

#[derive(Store, Clone, Debug)]
struct Todo { id: u32, title: String, done: bool }

let state = Store::new(Todos::default());
provide_context(state);

let filter = state.filter();                       // a field: reactive access to one part only
filter.set(Filter::Active);

view! {
    <For each=move || state.items() key=|row| row.read().id let(row)>
        <TodoRow todo=row/>
    </For>
}

#[component]
fn TodoRow(#[prop(into)] todo: Field<Todo>) -> impl IntoView {
    let done = todo.done();
    view! {
        <input type="checkbox" prop:checked=move || done.get()
            on:change:target=move |ev| done.set(ev.target().checked())/>
        {move || todo.title().get()}
    }
}

for row in state.items().iter_unkeyed() { *row.done().write() = true; }   // bulk update
```

- `#[derive(Store)]` generates a `<Name>StoreFields` trait with one accessor per field. Writing one field notifies
  that field, its parents and children, not its siblings.
- Import the generated trait (`use crate::state::TodoStoreFields;`) wherever the accessors are called from another
  module. A keyed row (`AtKeyed`) converts into `Field<T>` with `.into()` or a `#[prop(into)]` prop.
- The top level must be a struct. Add `#[store(key: K = |item| ...)]` to a `Vec` field for keyed access that
  follows rows through reorders.

## Async

| Tool                        | When                                                           | Runs                         |
|-----------------------------|----------------------------------------------------------------|------------------------------|
| `spawn_local(async { ... })`| Fire a future from an event handler; no tracking               | Browser (or server task)     |
| `Resource::new(source, fetcher)` | Load data that depends on signals; SSR-friendly, value serialized to the client | Server first, then browser on change |
| `Resource::new_blocking`    | Like `Resource`, but holds the HTML stream until it resolves (for `<title>`/meta) | Server |
| `LocalResource::new(fetcher)` | Browser-only or `!Send` futures (gloo-net, web APIs), or CSR | Browser only                 |
| `OnceResource::new(fut)`    | Load once                                                      |                              |
| `Action::new(\|input: &T\| ...)` | Run a mutation on demand and track its state              |                              |
| `Action::new_local`, `Action::new_unsync` | Same, for `!Send` futures (CSR with gloo-net)  |                              |
| `ServerAction::<Fn>::new()` | Action bound to a `#[server]` function, works with `<ActionForm/>` |                         |

### Resources

```rust
let page = RwSignal::new(1);
// source is tracked and memoized; the fetcher is not tracked
let todos = Resource::new(move || page.get(), |page| async move { list_todos(page).await });
// CSR calling a JSON API with a !Send client
let todos = LocalResource::new(move || fetch_todos(page.get()));

todos.refetch();                // reload on demand
todos.get();                    // Option<T>: None while loading
```

`Resource` values must be serializable (serde by default). Prefer it over `LocalResource` in SSR apps: loading
starts on the server and the result is embedded in the HTML.

### Suspense and Transition

```rust
view! {
    <Suspense fallback=|| view! { <p>"Loading..."</p> }>
        {move || Suspend::new(async move {
            let todos = todos.await;                     // await resources inside Suspend
            view! { <TodoList todos/> }
        })}
    </Suspense>

    <Transition fallback=|| "Loading...">              // shows old content while reloading
        {move || todos.get().map(|t| view! { <TodoList todos=t/> })}
    </Transition>

    <Await future=load_stats() let:stats>               // one-shot future, no fallback
        <p>{stats.count} " items"</p>
    </Await>
}
```

Every resource read under a `<Suspense/>` registers with it; the fallback shows until all have loaded. `<Suspense/>`
falls back on every reload, `<Transition/>` only the first time.

### Actions

```rust
let add = Action::new(|title: &String| {
    let title = title.clone();                 // the future must be 'static
    async move { add_todo(title).await }
});

add.dispatch("Buy milk".to_string());          // from any synchronous code
let pending = add.pending();                   // Memo<bool>
let last_input = add.input();                  // value being submitted, for optimistic UI
let result = add.value();                      // latest Option<Result<..>>
```

Pass several arguments as a tuple or struct. Re-fetch dependent data by making the resource source read the
action's version: `Resource::new(move || add.version().get(), |_| list_todos())`.

### Hydration and async side effects

- During hydration a `Resource` fetcher does not run; its value comes from the HTML. Do not set signals from inside
  a fetcher: return the value and `.await` it in a `Suspend` instead.
- Several `Suspend`s writing one signal may run in a different order on server and client. Keep them pure.
- Server functions called while rendering on the server see the app's context; called over HTTP they only see what
  `.leptos_routes_with_context` provides.

## How the graph works (short)

Sources (signals) feed memos, which feed effects. A change marks downstream nodes dirty or "check"; effects then pull
and re-run only if a memo's value actually changed. Memos cost an equality check and memory, so do not wrap cheap
derivations in them; memoize right before an expensive effect or computation.
