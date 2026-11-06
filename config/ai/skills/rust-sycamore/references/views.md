# Views and components

Write every view with the `view!` DSL. Sycamore also has a builder API (`div().children(...)`); do not use it, so
that all views read the same way and match the book.

Book chapters (`docs/next/` at tag `0.9.4`): `introduction/your-first-app.md`, `introduction/adding-state.md`,
`introduction/rendering-lists.md`, `guide/view-dsl.md`, `guide/data-binding.md`, `guide/node-ref.md`,
`guide/attribute-passthrough.md`. Every form below is taken from those chapters, the
macro's pass tests (`packages/sycamore-macro/tests/view/`) or a working app.

## Elements and text

```rust
view! {
    div(class="card", id="main") {
        h1 { "Hello, " (name) "!" }      // text literals and (expr) interpolation side by side
        my-custom-element {}             // custom elements: dashed tag names work
        br {}                            // void elements are written like any other
    }
}
```

- `view!` returns a `View`. Several top-level nodes are fine; `view! {}` (or `View::default()`) is empty.
- Interpolation accepts anything `Into<View>`: strings, numbers, `View`s, `Vec<View>`, `Option<View>`, signals,
  and closures returning those. `(todos_view)` where `todos_view: Vec<View>` renders a static list.

## What becomes reactive

The macro decides per expression (`is_dyn` in `sycamore-view-parser/src/codegen.rs`):

| Expression                                   | Treated as | Effect                                  |
|----------------------------------------------|------------|-----------------------------------------|
| Literal, plain path (`value`, `signal`), closure, field access (`todos.error`) | static | evaluated once |
| Method call, function call, macro, binary op, block containing any of these | dynamic | wrapped in `move \|\| ...`, re-run when what it reads changes |
| `if`/`match` whose condition, scrutinee, patterns or arms are dynamic        | dynamic | whole branch rebuilt when the condition changes |

So:

- `(count)` with `count: Signal<i32>` is "static" as an expression but a signal *is* a dynamic view: it updates.
- `(count.get() + 1)` and `if editing.get() { ... }` are wrapped in closures and update.
- `(value)` with `value: i32` and `class=if completed { "done" } else { "" }` with `completed: bool` are evaluated
  once, which is right for values that never change in that component.
- Because dynamic parts become `move` closures, a non-`Copy` local used in them is moved in. Using it in two such
  closures, or once in a closure nested in another, fails with "cannot move out of ... a captured variable in an
  `FnMut` closure". Put the value in a signal (`Copy`) or clone it into a local before the `view!`.

## Attributes

```rust
view! {
    p(class="my-class", id="p1", aria-label="My paragraph", "custom_attribute"="foo")
    button(r#type="submit", disabled=adding) { "Add" }            // adding: Signal<bool> stays reactive
    a(href=filter_href(option),
      aria-current=move || (current.get() == option).then_some("page")) { "All" }
    input(maxlength=MAX.to_string())                                 // stringy attribute: give it a String
    div(data-id=maybe_id)                                            // Option: None leaves the attribute out
    div(dangerously_set_inner_html="<b>trusted</b>")                 // never with user input
}
```

- HTML attributes are type-checked per element: booleans (`disabled`, `required`,
  `checked`, `hidden`) take `impl Into<MaybeDyn<bool>>`, so a `bool`, a `Signal<bool>` or a `move ||` closure;
  stringy ones take `&'static str`, `String`, `Option<_>`, a signal or a closure of those.
- `r#type`, `r#for`, `r#as`, `r#loop`, `r#ref` for Rust keywords.
- Dashed names (`aria-*`, `data-*`) are set as string attributes. A name that is not valid HTML goes in quotes.
- Known bug in 0.9.4: `autofocus` on `input` is "multiple applicable items in scope" (it is declared as a global
  attribute and an input attribute). Focus with a `NodeRef` in `on_mount` instead.

## Properties, binding, events, refs

```rust
let text = create_signal(String::new());
let node = create_node_ref();
view! {
    input(r#type="checkbox", prop:indeterminate=true)    // DOM property, not attribute
    input(bind:value=text)                               // two-way: Signal<String>, listens to `input`
    input(r#type="number", bind:valueAsNumber=amount)    // Signal<f64>
    input(r#type="checkbox", bind:checked=done)          // Signal<bool>, listens to `change`
    input(r#ref=node, on:keydown=move |ev: web_sys::KeyboardEvent| { /* ev.key() */ })
    form(on:submit=move |ev: web_sys::SubmitEvent| ev.prevent_default())
    button(on:click=move |_| count.set(0)) { "Reset" }
}
```

- `bind:` supports `value` (String), `valueAsNumber` (f64) and `checked` (bool) only.
- `on:<event>` takes a closure; the argument type is fixed per event (`click`: `MouseEvent`, `keydown`:
  `KeyboardEvent`, `submit`: `SubmitEvent`, `blur`/`focus`: `FocusEvent`, `input`/`change`: `Event`). Name it
  in the closure when you call methods on it; the type must be enabled in `web-sys` features if you import it.
- Get the element behind an event with `ev.target().unwrap().unchecked_into::<HtmlInputElement>()`
  (`JsCast` from `sycamore::web::wasm_bindgen`).
- `NodeRef::get()` panics until the node is mounted (and always on the server): use it in `on_mount` or handlers.

## Control flow

Since 0.9.4 `if`, `if let`, `else if` and `match` may be written directly in `view!`; branch bodies are view
nodes:

```rust
view! {
    if !error.with(String::is_empty) {
        p(role="alert") { (error) }
    }
    if let Load::Failed(message) = load.get_clone() {
        p { (message) }
    }
    match route.get_clone() {
        Route::Home => HomePage(),
        Route::Hello { name } => { h1 { "Hello, " (name) } },
        Route::Query => ({                     // Rust before the view: a parenthesised block
            let q = use_search_query("q");
            view! { p { (q.get_clone().unwrap_or_default()) } }
        }),
        Route::NotFound => {},                 // empty arm
    }
}
```

The older form, `(if cond { view! { ... } } else { view! {} })`, still works and is what most pre-0.9.4 code
uses. A whole branch is rebuilt when the condition changes, and its scope (signals, effects, tasks) is disposed.

## Components and props

```rust
#[component]
fn App() -> View { view! { Hello(name="Sycamore".into()) } }

#[derive(Props)]
struct HelloProps { name: String }

#[component]
fn Hello(props: HelloProps) -> View { view! { p { "Hello, " (props.name) } } }

// Usually shorter: inline props generate the struct.
#[component(inline_props)]
fn Counter(value: ReadSignal<i32>, #[prop(default)] step: i32) -> View { ... }

#[component(inline_props)]
fn Display(#[prop(setter(into))] value: MaybeDyn<i32>) -> View { ... }   // accepts a value, signal or closure

#[component(inline_props)]
fn Wrapper(children: Children) -> View { view! { div { (children) } } }

#[component(inline_props)]
fn Row<F>(todo: Todo, on_remove: F) -> View where F: Fn(Uuid) + Copy + 'static { ... }
```

- Call a component like an element: `Hello(name=...)`, `Wrapper { p { "child" } }`, `Counter()` or `Counter {}`
  with no props. In a `match` arm, `=> HomePage()` is enough.
- Props are moved in; pass signals (`Copy`) for shared state, `*signal` to pass a `Signal` as a `ReadSignal`.
- Generic props (callbacks) need `Copy + 'static` if they are used in more than one closure inside the component.
- Missing or misspelled props are compile errors pointing at the call.
- Async components (`#[component] async fn Data() -> View`) are allowed under a `Suspense`; see
  [async-and-api.md](async-and-api.md).

## Attribute passthrough

```rust
#[component(inline_props)]
fn Button(
    #[prop(attributes(html, button))] attributes: Attributes,
    children: Children,
) -> View {
    view! { button(class="btn", ..attributes) { (children) } }
}

view! { Button(id="save", on:click=|_| {}, disabled=true) { "Save" } }
```

Attributes passed to a component are *not* wrapped in closures automatically: pass `move || ...` yourself for a
reactive one. To intercept one (to add a class), declare it as a normal prop (`class: StringAttribute`).

## Lists

```rust
// Keyed: diffs by key. A row is reused while its key exists, even if the item changed.
Keyed(
    list=todos,                                        // Signal<Vec<T>>, ReadSignal, or closure
    view=move |todo| view! { TodoItem(todo=todo, todos=todos) },
    key=|todo| (todo.id, todo.updated_at),             // id + version: a changed todo gets a fresh row
)

// Indexed: diffs by position; a row is rebuilt when the item at its index changes (PartialEq).
Indexed(list=items, view=|item| view! { li { (item) } })
```

- `T: PartialEq + Clone + 'static`; `Keyed`'s key: `Hash + Eq`.
- Keying by id alone and replacing an item with an updated copy leaves the old row on screen. Either key by id and
  version, use `Indexed`, or make the changing fields `Signal`s (nested reactivity).
- Signals created per item (nested reactivity) live in the scope that created them, usually the parent: dispose
  them in the row with `on_cleanup(move || todo.task.dispose())` or they leak. Signals created *inside* the row's
  `view` closure belong to the row and are disposed with it.
- Every row gets its own child scope. When a row is removed or replaced, its scope, signals and
  `spawn_local_scoped` tasks are disposed: see [pitfalls.md](pitfalls.md#signal-was-disposed).

## `Show`

```rust
use sycamore::web::Show;
view! { Show(when=is_open) { Details() } }       // keeps the children's nodes, toggles them in and out
```

`Show` builds its children once and moves them in and out of the DOM; a bare `if` rebuilds the branch each time.
