# Reactivity, scopes and context

Book chapters: `introduction/adding-state.md`, `introduction/rendering-lists.md` (nested reactivity),
`guide/contexts.md`, `guide/tweened.md`. The book's reactive-graph page is a stub; the source of truth is
`packages/sycamore-reactive/src/` (`signals.rs`, `memos.rs`, `effects.rs`, `root.rs`, `node.rs`, `context.rs`).
Everything here is in `sycamore::prelude::*`.

## Signals

```rust
let count = create_signal(0);                 // Signal<i32>: Copy, whatever the T
let name = create_signal(String::new());
let read_only: ReadSignal<i32> = *count;      // Signal derefs to ReadSignal
```

| Read                                   | Behaviour                                                   |
|----------------------------------------|-------------------------------------------------------------|
| `.get()`                               | Copy the value out and track it; needs `T: Copy`            |
| `.get_clone()`                         | Clone and track                                             |
| `.with(\|v\| ...)`                     | Borrow and track, no clone: `name.with(String::is_empty)`   |
| `.get_untracked()`, `.get_clone_untracked()`, `.with_untracked(...)` | Same without subscribing |
| `.track()`                             | Subscribe without reading                                   |
| `.map(\|v\| ...)` (on `ReadSignal`)     | A derived `ReadSignal`                                      |
| `.is_alive()`                          | `false` once disposed                                       |

| Write                                  | Behaviour                                                   |
|----------------------------------------|-------------------------------------------------------------|
| `.set(v)`                              | Replace and notify (always, even if equal)                  |
| `.update(\|v\| ...)`                   | Mutate in place and notify: `todos.update(\|t\| t.push(x))` |
| `.set_fn(\|old\| new)`                 | Compute from the old value                                  |
| `.replace(v)`, `.take()`               | Return the old value                                        |
| `.set_silent(v)`, `.update_silent(...)`, `.replace_silent`, `.take_silent` | Change without notifying |
| `.split()`                             | `(ReadSignal<T>, impl Fn(T) -> T)`                          |
| `+= -= *= /=`                          | On `let mut s = create_signal(0)`: `move \|_\| s += 1`      |
| `.dispose()`                           | Free it now                                                 |

`Signal<T>` displays as its value (`console_log!("{count}")`) and is `Into<View>` when `T` is, so `(count)` in a
view is a live text node.

Read untracked inside event handlers and async tasks: tracking there subscribes nothing useful and, inside an
effect, would make the effect depend on it.

## Derived state, memos and selectors

```rust
let doubled = move || count.get() * 2;                      // derived closure: recomputed on every read
let total = create_memo(move || todos.with(Vec::len));      // cached; notifies on EVERY recompute
let is_empty = create_selector(move || name.with(String::is_empty)); // cached; notifies only when != (PartialEq)
let same_len = create_selector_with(move || items.get_clone(), |a, b| a.len() == b.len());
```

In 0.9.4 `create_memo` is `create_selector_with(f, |_, _| false)`: every input change propagates, even if the
result is equal. Prefer `create_selector` for anything that triggers work downstream (a fetch, a big list), such
as a filter parsed from the URL.

## Effects

```rust
create_effect(move || console_log!("count = {}", count.get()));   // runs now, then on every change

create_effect(on(filter, move || {        // explicit dependencies; the body is untracked
    load(filter.get_untracked());
}));
```

- An effect is a memo returning `()`. It runs synchronously at creation and after each change of what it read.
- Each run first **disposes everything the previous run created**: child scopes, signals, nested effects, and
  tasks started with `spawn_local_scoped`. That makes "start a request in an effect" cancel the stale request.
- Do not write to signals that the effect itself reads. To mirror state, derive it (closure, memo, selector).
  Writing other signals from an effect is fine when it is truly a side effect (a fetch result arriving).
- `create_effect_initial` runs a different closure on the first run.

## Batching and untracking

```rust
batch(|| {                 // dependents run once, after the closure
    items.set(list);
    load.set(Load::Loaded);
});
let n = untrack(|| count.get());     // read without tracking inside a tracked context
```

## Scopes and disposal

Every reactive node (signal, memo, effect) belongs to the scope that is current when it is created. Components,
`Keyed`/`Indexed` rows, dynamic `if`/`match` branches and each run of an effect are scopes. When a scope is
disposed, everything in it is disposed too.

```rust
on_cleanup(move || todo.title.dispose());          // runs when the current scope is disposed
on_mount(move || input_ref.get().unchecked_into::<HtmlInputElement>().focus().unwrap()); // after mount, browser only

let handle = create_root(|| { /* app or test code */ });   // a new root, independent of the app
handle.dispose();
let child = create_child_scope(|| { /* ... */ });          // NodeHandle
child.dispose();
let scope = use_current_scope();                           // run code in it later: scope.run_in(|| ...)
```

- Accessing a disposed signal panics with "signal was disposed" (signals.rs). In WASM a panic aborts the module,
  so the whole page stops reacting. The common trigger is an async task in a list row that finishes by writing
  the row's own signals *after* it removed or replaced that row. See [pitfalls.md](pitfalls.md).
- Signals created in a parent for each list item (nested reactivity) outlive the row; dispose them in
  `on_cleanup` inside the row, or they leak until the parent goes.
- `try_use_context` and `is_alive()` exist, but the fix is almost always ordering: update the scope's own state
  first, and make the call that disposes it the last thing.

## Context

```rust
#[derive(Clone, Copy)]
struct Todos { items: Signal<Vec<Todo>>, error: Signal<String> }

provide_context(Todos { items: create_signal(vec![]), error: create_signal(String::new()) });
let todos = use_context::<Todos>();                         // panics if missing
let maybe = try_use_context::<Todos>();
let theme = use_context_or_else(|| Theme::default());
provide_context_in_new_scope(value, || view! { Child() });  // shadow a context for a subtree
```

Context values are not reactive by themselves: put signals inside. A small `Copy` struct of signals with methods
(`run`, `place`, `remove`) is a good way to share page state, whether through context or as a prop.

## Reducers and tweened signals

```rust
enum Msg { Increment, Decrement }
let (state, dispatch) = create_reducer(0, |&s, msg: Msg| match msg {
    Msg::Increment => s + 1,
    Msg::Decrement => s - 1,
});
dispatch(Msg::Increment);

use sycamore::{easing, motion::create_tweened_signal};
let progress = create_tweened_signal(0.0f32, std::time::Duration::from_millis(250), easing::quad_out);
progress.set(100.0);                                         // animates to 100 over 250 ms
```

## Testing reactive code natively

Reactive code runs without a browser:

```rust
#[test]
fn doubles() {
    let _ = create_root(|| {
        let count = create_signal(1);
        let doubled = create_memo(move || count.get() * 2);
        count.set(2);
        assert_eq!(doubled.get(), 4);
    });
}
```
