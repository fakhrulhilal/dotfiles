# Views and components

Book chapters (`src/` of <https://github.com/leptos-rs/book>): `view/01_basic_component.md` to `view/09_component_children.md`, `view/builder.md`,
`interlude_projecting_children.md`. Docs: <https://docs.rs/leptos/latest/leptos/macro.view.html>.

## `view!` syntax

```rust
use leptos::prelude::*;

#[component]
fn Counter(initial: i32) -> impl IntoView {
    let (count, set_count) = signal(initial);
    let is_odd = move || count.get() % 2 == 1;

    view! {
        <button
            on:click=move |_| *set_count.write() += 1
            class:odd=is_odd                       // toggles one class
            class=("btn-20", is_odd)               // class names the macro cannot parse
            style:left=move || format!("{}px", count.get())
            style=("--columns", move || count.get().to_string())
            aria-label="Increment"
        >
            "Count: " {count}
        </button>
        <progress max="50" value=count/>
        <p>{move || if is_odd() { "Odd" } else { "Even" }}</p>
    }
}
```

- Text is quoted. Unquoted text works but has spacing problems around punctuation; quote it.
- `{expr}` inserts a value. A signal or closure is reactive; anything else renders once.
- All elements close, void ones included: `<br/>`, `<input/>`, `<img/>`.
- Comments are `//` Rust comments.
- `inner_html=html` sets raw HTML without escaping. Only for trusted content.
- A view can return several root nodes; components return `impl IntoView`.
- `Option<T>` and `Result<T, E>` render: `None` and `Err` render nothing (an `Err` also trips the nearest
  `<ErrorBoundary/>`).

## Attributes, properties and events

| Written                               | Meaning                                                              |
|---------------------------------------|----------------------------------------------------------------------|
| `value="x"`, `value=count`            | HTML attribute; reactive when given a signal or closure              |
| `prop:value=name`                     | DOM property; use for the current value of inputs, `prop:checked`    |
| `bind:value=name`                     | Two-way binding for text inputs (`RwSignal` or `(getter, setter)`)    |
| `bind:checked=flag`                   | Two-way binding for checkboxes                                       |
| `bind:group=color`                    | Two-way binding for a radio group                                    |
| `class:name=bool_or_fn`               | Toggle one class                                                     |
| `style:prop=value_or_fn`              | Set one CSS property                                                 |
| `on:click=move \|ev\| ...`            | Event listener; `ev` is the typed event (`MouseEvent`)               |
| `on:input:target=move \|ev\| ev.target().value()` | Typed access to the target element                       |
| `on:submit:capture=...`               | Listener in the capture phase, runs before the element's own handler |
| `node_ref=input_ref`                  | Fill a `NodeRef<html::Input>` with the element                        |
| `oninput="this.form.requestSubmit()"` | Plain HTML attribute (a JavaScript string), not a Rust listener      |

## Components and props

```rust
/// Shows progress toward a goal.
#[component]
pub fn ProgressBar(
    /// Maximum value.
    #[prop(default = 100)]
    max: u16,
    /// Accepts a signal, memo, plain value or closure.
    #[prop(into)]
    progress: Signal<i32>,
    #[prop(optional)] label: Option<String>,
    #[prop(optional, into)] class: MaybeProp<String>,
) -> impl IntoView {
    view! { <progress class=class max=max value=progress/> {label} }
}

// usage
view! {
    <ProgressBar progress=count/>
    <ProgressBar progress=move || count.get() * 2 max=50/>
}
```

- Props are function arguments; the macro builds a props struct. Names are `snake_case`; components are
  `PascalCase`.
- A prop that changes over time must be reactive (`Signal<T>`, `ReadSignal<T>`, a closure). A plain `i32` prop is
  read once.
- `#[prop(into)]` calls `.into()`. In 0.8 a `Signal<T>` prop with `into` accepts a value, any signal type, a memo,
  or a closure. `Signal::derive(f)` still works and is needed outside props.
- `#[prop(optional)]` uses `Default::default()`; `#[prop(default = expr)]` sets another default;
  `#[prop(optional, into)] x: MaybeProp<T>` gives an optional reactive value read with `x.get()` (an `Option<T>`).
- `#[prop(marker)] _ty: PhantomData<T>` for generics only used in types; use generics in the view as
  `<SizeOf<usize>/>` (no turbofish, no nested generics).
- Optional generic props cannot infer their type; use `Option<Box<dyn Fn() -> i32 + Send + Sync>>`.
- Doc comments on the function and each prop show up in rust-analyzer.
- `#[component(transparent)]` returns data rather than a view (used for route fragments).

Attributes on a component are spread onto every top-level element it returns:

```rust
view! {
    <MyButton
        label="Save"                       // prop
        class:primary=true                 // class:, style:, prop:, on: work as on elements
        on:click=move |_| save()
        attr:id="save"                     // plain HTML attribute
        {..}                               // everything after this is an HTML attribute
        title="Save the draft"
    />
}
```

Use `AttributeInterceptor` when the attributes should land on one inner element instead.

## Parent-child communication

1. Pass a `WriteSignal<T>` down. Simple, but scattering setters makes changes hard to follow.
2. Pass a callback prop: `on_toggle: impl FnMut(MouseEvent) + 'static`, or `Callback<T>` / `UnsyncCallback<T>` when
   it is stored or cloned. The parent owns the mutation.
3. Put `on:click=...` on the component itself when the event maps to a DOM event of its root elements.
4. `provide_context(value)` in an ancestor and `use_context::<T>()` / `expect_context::<T>()` below. Context is
   keyed by type, so wrap plain types in a newtype. It is a runtime lookup, not checked by the compiler.

## Children, render props and slots

```rust
#[component]
fn Panel<F, IV>(header: F, children: Children) -> impl IntoView
where
    F: Fn() -> IV,
    IV: IntoView,
{
    view! { <section><header>{header()}</header>{children()}</section> }
}

view! { <Panel header=|| view! { <h2>"Title"</h2> }>"Body"</Panel> }
```

| Type               | Use                                                      |
|--------------------|----------------------------------------------------------|
| `Children`         | `Box<dyn FnOnce() -> AnyView>`, called once              |
| `ChildrenFn`       | Called many times (inside `<Show/>`, `<Suspense/>`)      |
| `ChildrenMut`      | `FnMut` version                                          |
| `ChildrenFragment` | Returns a `Fragment`; iterate `.nodes` to wrap each child |
| `TypedChildren<T>` | Keeps the concrete view type                             |

Slots give typed, named children:

```rust
#[slot]
struct Then { children: ChildrenFn }

#[component]
fn If(condition: Signal<bool>, then: Then) -> impl IntoView {
    move || condition.get().then(|| (then.children)())
}

view! { <If condition=flag><Then slot>"Shown when true"</Then></If> }
```

`slot` alone uses the snake-case struct name; `slot:then_slot` names the prop. Event listeners cannot go on a slot;
wrap its content in an element.

Projecting children through components that take `ChildrenFn` fails with "expected `Fn`, found `FnOnce`". Store
them first (`let children = StoredValue::new(children);` then `children.read_value()()`), or use `clone:name` on the
inner component to clone a captured value before it moves into the children closure.

## Control flow

```rust
let message = move || is_odd().then(|| "Ding!");          // Option renders or not
let label = move || match value.get() {
    0 => "Zero",
    n if n % 2 == 1 => "Odd",
    _ => "Even",
};

view! {
    <Show when=move || value.get() > 5 fallback=|| view! { <Small/> }>
        <Big/>
    </Show>
    {move || match mode.get() {
        Mode::List => view! { <List/> }.into_any(),
        Mode::Grid => view! { <Grid/> }.into_any(),
    }}
}
```

- Plain `move || if ...` re-runs (and rebuilds its branch) on every change of what it reads. `<Show/>` memoizes the
  condition and only switches when it flips; use it when a branch is costly.
- Different element types in branches do not unify: use `.into_any()` (type-erased `AnyView`) or
  `Either::Left/Right`, `EitherOf3`, `EitherOf4`...

## Lists

```rust
// static list
let items = values.into_iter().map(|n| view! { <li>{n}</li> }).collect_view();

// keyed dynamic list
view! {
    <For each=move || todos.get() key=|todo| todo.id let(todo)>
        <TodoRow todo/>
    </For>
    <ForEnumerate each=move || rows.get() key=|r| r.id let(index, row)>
        <p>{move || index.get()} ". " {row.title}</p>
    </ForEnumerate>
}
```

- The key must be stable and unique (an id, never the index).
- A row only re-renders when its key changes. To update fields in place, pick one:
  1. Put the changing value in the key (simple, rebuilds the row each change).
  2. Make the field a signal (`value: RwSignal<i32>`); most efficient.
  3. Per-row `Memo` reading back into the list by index, with `<ForEnumerate/>`.
  4. A `reactive_stores` store with a keyed field (see reactivity.md); fine-grained without changing the data type.
- `move || vec.get().into_iter().map(...).collect_view()` is unkeyed; fine for append-only lists.

## Forms and inputs

```rust
let name = RwSignal::new(String::new());
let agree = RwSignal::new(false);

view! {
    <input type="text" bind:value=name/>
    // or explicitly:
    <input type="text" prop:value=name on:input:target=move |ev| name.set(ev.target().value())/>
    <input type="checkbox" bind:checked=agree/>
    <textarea prop:value=move || body.get() on:input:target=move |ev| body.set(ev.target().value())>
        {body}  // initial value for server rendering
    </textarea>
    <select prop:value=move || choice.get() on:change:target=move |ev| choice.set(ev.target().value())>
        <option value="a">"A"</option>
        <option value="b">"B"</option>
    </select>
}
```

Uncontrolled input read on submit:

```rust
let input: NodeRef<html::Input> = NodeRef::new();
let on_submit = move |ev: SubmitEvent| {
    ev.prevent_default();
    let value = input.get().expect("mounted").value();
    add.dispatch(value);
};
view! {
    <form on:submit=on_submit>
        <input type="text" node_ref=input/>
        <button type="submit">"Add"</button>
    </form>
}
```

Parse input yourself even with `type="number"`; browsers differ.

## Errors

```rust
let (value, set_value) = signal(Ok::<i32, ParseIntError>(0));
view! {
    <input on:input:target=move |ev| set_value.set(ev.target().value().parse::<i32>())/>
    <ErrorBoundary fallback=|errors| view! {
        <ul>
            {move || errors.get().into_iter().map(|(_, e)| view! { <li>{e.to_string()}</li> }).collect_view()}
        </ul>
    }>
        <p>"You entered " {value}</p>
    </ErrorBoundary>
}
```

The boundary recovers by itself once the `Err` turns back into `Ok`.

## Builder syntax (no macros)

```rust
use leptos::{ev, html::*};

pub fn counter(step: i32) -> impl IntoView {
    let (count, set_count) = signal(0);
    div().child((
        button().on(ev::click, move |_| set_count.set(0)).child("Clear"),
        span().attr("data-count", move || count.get().to_string()).child(("Value: ", count)),
        button().on(ev::click, move |_| *set_count.write() += step).child("+1"),
    ))
}

// using a component without the macro
Show(ShowProps::builder().when(move || count.get() > 5).fallback(|| "small").children(ToChildren::to_children(|| "big")).build())
```

Use rust-analyzer's "expand macro recursively" or `cargo expand` to see what `view!` generates.
