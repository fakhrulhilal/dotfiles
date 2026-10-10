# Browser runtime

Upstream guides: `docs/runtime.md`, `docs/runtime/expr.md`, `docs/runtime/record.md`, `docs/runtime/procedure.md`,
`docs/runtime/shard.md`, `docs/runtime/link.md`. Examples: `examples/runtime` and
`demos/coffee-shop/src/app/menu/drink.rs`.

The runtime is the most experimental part of Topcoat. Expressions support a limited set of types and operations.

## Setup

```rust
module_router!()
    .discover()                             // registers procedures and shards
    .assets(AssetBundle::load().unwrap())   // serves the browser script
    .runtime()                              // register application layers before this
    .build()
```

Put `topcoat::runtime::script()` in the document `<head>`. Without `.discover()`, register each procedure and shard by
name with `.route(name)`.

## Signals, expressions, events, bindings

```rust
use topcoat::{
    Result,
    context::Cx,
    runtime::{Event, signal},
    view::{View, component, view},
};

#[component]
async fn counter(cx: &Cx) -> Result<impl View> {
    let count = signal(cx, || 0usize);
    let name = signal(cx, String::new);
    let open = signal(cx, || false);

    Ok(view! {
        <button @click=$(|_e| count.increment())>"+1"</button>
        <p>"Count: " $(count.get())</p>

        <input :value=$(name.get()) @input=$(|e: Event| name.set(e.target.value))>
        <p>"Hello, " $(name.get()) "!"</p>

        <button @click=$(|_e| open.toggle())>"Details"</button>
        <p :hidden=$(!open.get())>"More text"</p>
    })
}
```

| Syntax          | Meaning                                                                             |
|-----------------|-------------------------------------------------------------------------------------|
| `signal(cx, f)` | browser state; the server computes the initial value                                |
| `$(expr)`       | runtime expression (`runtime::expr!`): server value first, reactive in the browser  |
| `@event=$(...)` | DOM event handler; a closure, or a JavaScript string such as `@click="alert('hi')"` |
| `:attr=$(...)`  | bound attribute, updated when a signal it reads changes                             |

- Signals are cheap to clone. Components take one as `&Signal<T>`.
- Signal methods: `get`, `set`, `read`, plus `toggle` (bool), `increment`/`decrement` (numbers), `push_str` (String).
- An identifier not defined inside the expression is captured by clone and sent to the browser as a snapshot from that
  render. Never capture a secret.
- Inside a component call, pass handlers through attributes:
  `button(attrs: attributes! { @click=$(|_e: Event| quantity.increment()) }, "+")`.

## The expression vocabulary

Types: `f64`, Rust integer types, `bool`, `String`/`&str`, `Option<T>`, `Result<T, E>`, `Vec<T>`/arrays/slices, tuples,
`#[record]` structs and `Signal`.

Syntax: literals, the listed operators, method calls, field access, `Some`/`None`/`Ok`/`Err`, tuple and record
literals, blocks with `let`, `if`/`else`, closures (optionally `async`) with `.await`, `loop`, `while`, `break`,
`continue`, `return`. Anything else is a compile error at the expression.

Things that surprise people:

- Unsuffixed integer literals are `usize`. Write `42u64` or `-1i32` for another type. Both operands must share a
  type. A float needs a decimal point (`1.0`).
- Integer overflow, division by zero and remainder by zero panic in debug and release.
- String `len` counts UTF-8 bytes. String methods are limited to `len`, `is_empty`, `trim*`, `starts_with`,
  `ends_with`, `contains`, `to_owned` and comparisons.
- Collections offer `len`, `is_empty`, `get`, `index`, `first`, `last`. `index` panics when out of bounds.
- Tuples and records cannot be compared. Records cannot be rendered directly; render their fields.
- A captured `Expr<T>` behaves as `T` in another expression and stays reactive:
  `let active = expr!(selected.get() == "overview");`.

Escape hatch: `raw!("${n}.toUpperCase()", n.to_uppercase())`. The first argument is JavaScript, with `${ident}`
inserting a binding. The second is the equivalent Rust for the server and must read the signals the JavaScript depends
on. Without it the expression is browser-only.

## Records

```rust
use topcoat::runtime::record;

#[record]
#[derive(Clone)]
struct Todo {
    title: String,
    done: bool,
}
```

A `#[record]` struct has named fields and no generics, with field types from the vocabulary (nested records allowed).
It can be used in expressions, signals and procedure inputs and outputs. Build it with `Todo { title, done: false }`
and borrow a field through a signal with `todo.read().title`. `Clone` is needed for `get` and `clone`. Capturing a
record exposes every field, private ones included. Validate records received from the browser.

## Procedures

```rust
use topcoat::{Result, context::Cx, runtime::procedure};

#[procedure]
async fn place_order(cx: &Cx, drink: String, quantity: f64) -> Result<String> {
    let user = require_auth(cx).await?;      // authorize here
    // validate drink and quantity here
    Ok(format!("{quantity} x {drink}"))
}

// In a view:
<button @click=$(async move |_e: Event| {
    let message = place_order(name.to_owned(), quantity.get()).await;
    confirmation.set(message);
})>"Order"</button>
```

- A procedure is an async server function with its own POST endpoint. Callers omit `cx` and receive the `Ok` value.
- Arguments and the `Ok` type must be in the vocabulary.
- A server `Err` makes the browser expression fail without exposing the error. To let the client handle a failure,
  return it as data: `Result<Result<String, String>>`.
- A call may only run in the browser. Evaluating one during server rendering panics, so keep calls inside event
  handler closures.
- The generated path changes when the function moves or is renamed. Pin it with `#[procedure("/api/place-order")]`;
  an explicit path cannot contain parameters.

## Shards

```rust
use topcoat::{Result, context::Cx, runtime::{Event, shard, signal}, view::{View, view}};

#[shard]
async fn search_results(cx: &Cx, query: String) -> Result<impl View> {
    let user = require_auth(cx).await?;      // page and layout guards do not run here
    let products = search_products(cx, user, &query).await?;
    Ok(view! {
        for product in products {
            <div id=(product.id)>(product.name)</div>
        }
    })
}

// In a view:
let query = signal(cx, String::new);
view! {
    <input :value=$(query.get()) @input=$(|e: Event| query.set(e.target.value))>
    search_results(query: $(query.get()))
}
```

- A shard is a component that re-renders on the server when its inputs change. It renders inline on first load, with
  no extra request.
- Each parameter accepts its type `T` or a runtime expression. Changes in the same tick share one request, and a newer
  request aborts a pending one.
- Elements are patched in place, matched by position and tag, or by `id` when present. Give reorderable items a stable
  `id`.
- Signals created inside a shard keep their values across re-renders. Reading one on the server makes only that shard
  re-render, which is how pagination or filtering stays local.
- A `Signal<T>` parameter shares the caller's handle (`limit: limit`). Passing the handle creates no dependency; a
  tracked read in the body does.
- `#[shard("/search/results")]` pins the endpoint.

## Tracked reads on the server

| Where and how                            | Effect                                           |
|------------------------------------------|--------------------------------------------------|
| `.get()` / `.read()` in server Rust      | the enclosing page or shard re-renders on change |
| `.get()` inside `$(...)`                 | browser-only update, no server render            |
| `.get_untracked()` / `.read_untracked()` | read on the server without causing re-renders    |

A page that reads a signal on the server re-renders as a whole when it changes. Move that read into a shard to limit
the re-render. On a re-render `signal` restores the value the browser sent, so treat it as user input.

## Navigation

```rust
use topcoat::runtime::link;

view! { link(href: href!(menu::page), "Menu") }
```

`runtime::link` opens server-rendered pages without a full document reload and keeps signals shared by both pages.
Prefetch defaults to `PrefetchMode::Intent` (hover, focus, touch); `Viewport` loads visible links and `Never` waits.
Override per link with `prefetch:`, per subtree with `cx.with(mode)`, or per app with `.prefetch(mode)`. For a custom
anchor use `link_attrs(cx, href!(page), prefetch_mode(cx))`. A plain `<a>` keeps normal navigation.

## Security checklist

- Validate every procedure argument, shard argument and restored signal value.
- Authorize inside every procedure and shard.
- Capture only values that are safe to show the user.
- Use POST routes or procedures for mutations, never GET.