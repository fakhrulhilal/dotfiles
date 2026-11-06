# Pitfalls

Each entry was met and fixed while writing a todo client on Sycamore 0.9.4 and sycamore-router 0.9.4 (Rust 1.99,
edition 2024, Trunk, Chromium under Playwright), or read from the 0.9.4 source where marked.

## Runtime failures

### "signal was disposed"

```text
panicked at .../sycamore-reactive-0.9.4/src/signals.rs:180:25:
signal was disposed
```

**Symptom:** after completing or deleting a todo, the page stops reacting. Links change the URL but nothing on the
page updates, inputs no longer save. The console shows the panic above once.

**Cause:** a list row started a task (`spawn_local_scoped`) and, when the API answered, called `todos.place(todo)`
(which replaced the item in the list; the list is keyed by id and `updated_at`, so the row was rebuilt) and then
`busy.set(false)` on the old row's signal. Replacing the item disposed the old row's scope and its signals within
the same poll, so the next write panicked. A panic in WASM leaves the reactive runtime unusable until reload.

**Fix:** update the row's own state first and make the call that disposes the row the last statement:

```rust
let saved = todos.run(api::update(id, changes)).await;
busy.set(false);                       // row still alive
if let Some(todo) = saved {
    todos.place(todo);                 // disposes this row: nothing after it
}
```

The same applies to removing a row, closing a branch (`if editing.get()`) from inside it, and navigating away from
a page inside its own handler.

### Back and Forward do not change the page when only the query differs

**Symptom:** `/?filter=completed` → Back → the URL is `/?filter=active`, the list and the highlighted filter are
still "Completed". Links between the same addresses work.

**Cause (router.rs):** `HistoryIntegration`'s `popstate` listener updates the router only when the *path* changed.
`use_search_query` tracks the path and an internal query signal that only link clicks touch.

**Fix:** listen to `popstate` as well and re-read `location.search`; code in
[routing.md](routing.md#query-strings).

### The list loads again when nothing changed

**Cause (memos.rs):** `create_memo` is `create_selector_with(f, |_, _| false)`, so it notifies on every
recomputation. A filter memo that tracks the router and `popstate` re-ran the list effect on every event.

**Fix:** `create_selector` (uses `PartialEq`).

### A changed item still shows its old values

**Cause:** `Keyed` reuses the row for a known key and never passes it the new item.

**Fix:** key by id and version (`key=|t| (t.id, t.updated_at)`), use `Indexed`, or make the changing fields
signals.

### A checkbox stays ticked after a failed save

**Cause:** the browser flips `checked` on click before the app's handler runs; if the API call fails nothing on
the page changes, so the box stays flipped.

**Fix:** on failure, set it back: `checkbox.set_checked(completed)` with the element taken from the event target.

### Links reload the whole page

**Cause:** the router's click handler only sits on the top-level nodes of its `view`. A link rendered outside the
`Router`, or (per the source's own TODO) under a top-level node that is replaced dynamically, is not intercepted.

**Fix:** keep static elements (`header`, `main`) at the top of the router view and put the `match` inside.

### Focus

`autofocus` cannot be written on `input` (below), and a box that appears after a click (a rename field) needs
focus anyway. Use a `NodeRef` and `on_mount`, inside the component that renders the input, so it runs each time
it appears:

```rust
let node = create_node_ref();
on_mount(move || {
    let input: HtmlInputElement = node.get().unchecked_into();
    let _ = input.focus();
    input.select();
});
view! { input(r#ref=node, bind:value=draft) }
```

Removing a focused input fires `blur` in some browsers; a "save on blur" handler must ignore a second call (check
an `editing` flag and a `busy` flag, both read untracked).

## Compile errors

| Message (abridged)                                                                 | Cause and fix                                                                          |
|------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|
| `multiple applicable items in scope ... multiple autofocus found` (candidates `HtmlInputAttributes` and `HtmlGlobalAttributes`) | 0.9.4 declares `autofocus` twice for `input`. Drop it and focus in `on_mount`. |
| `cannot move out of value, a captured variable in an FnMut closure` pointing into `view!` | A `String` (or other non-`Copy` value) is used in a dynamic part, which the macro wraps in a `move` closure, possibly nested in another. Put it in a signal and interpolate the signal, or clone into a local first. |
| `expected bool, found Signal<bool>` at `if flag { ... }` in `view!`                 | Control flow needs the value: `if flag.get() { ... }`.                                  |
| `cannot find trait JsCast` / `Closure`                                              | Import from `sycamore::web::wasm_bindgen::{JsCast, prelude::Closure}`, or add `wasm-bindgen`. |
| `the trait bound ...: Copy is not satisfied` for a callback prop used twice         | Bound it `F: Fn(..) + Copy + 'static`, and make what it captures `Copy` (signals, not `String`s). |
| `can't find crate for core ... wasm32-unknown-unknown target may not be installed`  | `rustup target add wasm32-unknown-unknown` for the toolchain in use (`rustup show active-toolchain`; `RUSTUP_TOOLCHAIN` may override it). |
| `this function takes 2 arguments but 1 argument was supplied` at `AppRoute::match_path(path)` | `match_path` takes `&self`: `AppRoute::default().match_path(path)`.                      |
| `unresolved import sycamore::futures`                                               | Enable the `suspense` feature.                                                          |
| `Show`/`Suspense` not found                                                         | They are in `sycamore::web`, not the prelude.                                           |

## Habits that catch these early

- Keep the browser console open while trying a change; reload after any panic before testing anything else.
- Try the flows that dispose things: delete, edit a keyed row, switch branches, change filters during a request,
  Back and Forward.
- End-to-end tests that toggle and then filter would have caught the disposal panic; one that presses Back after
  two filters catches the router gap.
