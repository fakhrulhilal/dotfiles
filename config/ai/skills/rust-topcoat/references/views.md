# Views, components and streaming

Upstream guides: `docs/view/view.md`, `docs/view/component.md`, `docs/view/props.md`, `docs/view/class.md`,
`docs/view/attributes.md`, `docs/view/live.md`. Examples: `examples/hello-world`, `examples/suspense`, `examples/live`.

## `view!` syntax

```rust
Ok(view! {
    <!DOCTYPE html>
    <article data-post-id=(post.id) class="card">
        let title = post.title.trim();

        <h1>"Post: " (title)</h1>
        <img src=(post.image) alt="">

        if post.published {
            <a href=(href!(page))>"Read"</a>
        } else {
            <span>"Draft"</span>
        }

        <ul>
            for tag in post.tags {
                <li>(tag)</li>
            }
        </ul>

        match post.author {
            Some(author) => {
                <p>(author.name)</p>
                <p>"Verified"</p>
            },
            None => "",
        }
    </article>
})
```

- Elements use real HTML names. Custom elements may contain dashes. Void elements have no closing tag; all others
  need a matching one.
- Text nodes are quoted. `(expr)` interpolates a value as a child or as an attribute value. Output is escaped.
- Attribute names may contain `-`, `:` and `.` (`aria-label`, `hx-get`). Rust keywords work: `type="button"`,
  `for="email"`.
- `<(tag) (attr)="x">...</(tag)>` gives dynamic element and attribute names.
- `if`, `for`, `match` and `let` work among children and also inside an attribute list, where the branches emit
  attributes.
- A `match` arm is one node. Wrap several sibling nodes in `{ ... }`.
- Comments are ordinary `//` Rust comments.

## Attributes

| Written                                               | Result                                    |
|-------------------------------------------------------|-------------------------------------------|
| `required=""`                                         | boolean attribute, always present         |
| `disabled=(is_disabled)`                              | present when `true`, omitted when `false` |
| `title=(maybe_title)`                                 | omitted when `None`                       |
| `aria-current=(current.then_some("page"))`            | value or omitted                          |
| `disabled="false"`                                    | still present, so still disabled          |
| `aria-expanded=(if open { "true" } else { "false" })` | enumerated attribute, needs strings       |

- Keep a fixed class list as `class="btn btn-primary"`.
- Compose with `class=(class!("btn", "opacity-50" if pending, "on" if active else "off", extra))`. Absent entries are
  skipped and an empty class attribute is omitted. There is no Tailwind conflict resolution.
- `attributes! { id="x" name="y" }` builds a collection that spreads with `<div (attrs)>`, consuming it. To merge a
  forwarded class: `class=(class!("base", attrs.remove("class")))` and then `(attrs)`.

## Components

```rust
use topcoat::{
    Result,
    context::Cx,
    view::{Child, View, component, view},
};

#[component]
async fn panel(
    cx: &Cx,                           // optional, injected, callers omit it
    title: &str,
    #[into] subtitle: String,          // accepts anything Into<String>
    #[default(3)] level: u8,           // optional with a fallback
    #[default] child: Child<'_>,       // children; #[default] allows calling without them
) -> Result<impl View> {
    Ok(view! {
        <section>
            <h2>(title)</h2>
            (child)
        </section>
    })
}

// Call site: named props with commas, then children without commas.
view! {
    panel(
        title: "Profile",
        subtitle: "Account",
        <p>"Details"</p>
        badge(label: "Active")
    )
}
```

- Components are async functions returning `Result<impl View>`, so they can await a database call directly.
- `#[derive(Props)]` gives a struct the same builder rules (`#[default]`, `#[into]`) with required fields checked at
  compile time.
- Generic components may need `T: Send + Sync` bounds.
- A recursive component, or branches returning different view types, box with `ViewExt::boxed()`.
- Outside a component, pass the context explicitly: `view! { cx => greeting(name: "World") }`.

## Laziness, ownership and concurrency

- A `view!` value runs its expressions only when rendered and captures variables by move. Clone into a separate
  binding when the value is also needed afterwards.
- Borrowed component parameters (`&str`) can be used directly because they outlive the render.
- Components in one view render concurrently. Markup appears in source order, but the order in which bodies run is
  unspecified. Never rely on one component having run before another, and never communicate through shared mutable
  state. Do ordered work first and interpolate the results.

## Keys

```rust
view! {
    #[key(post.id)]
    for post in posts {
        post_card(title: post.title)
    }
}
```

A key gives each iteration a stable identity for stateful components, shards and live regions. A missing key can
produce an error that names the loop. Also give reorderable DOM elements a stable `id`. A plain helper function that
needs its own identity per item takes `&cx.keyed(item.id)`.

## Status and headers from a view

```rust
view! {
    (StatusCode::NOT_FOUND)
    ((header::CACHE_CONTROL, HeaderValue::from_static("no-store")))
    <h1>"Page not found"</h1>
}
```

The first status in markup order wins, and the first declaration per header name wins. In a layout, declare before
`(slot)` to override the page or after it to supply a default. Nothing can change once streaming has started.

## Rendering to a string

`view.single().await?.render(cx)` (via `ViewExt`) renders non-live HTML. `.single()` panics on a live view;
`.first()` keeps the first emission and discards later updates.

## Streaming with `live!`

```rust
use topcoat::view::{View, emit, live, view};

view! {
    <h1>"Quote of the day"</h1>
    (live! {
        emit! { <p>"Loading..."</p> }?;
        let quote = fetch_quote().await;
        emit! { <blockquote>(quote)</blockquote> }
    })
}
```

- Each `emit!` replaces the region. The body is ordinary async Rust and returns `Result<EmitToken>`, so it normally
  ends with an emission. Use `?` on the earlier ones. Return `Ok(EmitToken)` only when the body cannot end with an
  emission; it must still emit at least once.
- The page waits for the first emission, so emit something that is ready immediately.
- `suspense(fallback: view! { <p>"Loading..."</p> }, slow_child())` covers the common loading case.
- `error_boundary(fallback: |error| Ok(view! { ... }), child())` swaps in a fallback when rendering fails.
- A live region is a `View`, so a component can return `live! { ... }` directly. In a plain function use
  `live! { cx => ... }`.

Long-lived updates use `runtime::connected(cx)`, which needs `.runtime()` and `topcoat::runtime::script()`:

```rust
(live! {
    let chat = app_context::<Chat>(cx);
    let mut changed = chat.subscribe();          // subscribe before reading
    loop {
        let token = emit! { <ul> for m in chat.messages() { <li>(m)</li> } </ul> }?;
        if !connected(cx) {
            break Ok(token);                     // HTTP render: emit once and finish
        }
        changed.recv().await.ok();               // connected render: wait for changes
    }
})
```

- During the HTTP render `connected(cx)` returns `false` and asks the browser to open a WebSocket. The page or shard
  then renders again with `true`. Keep every indefinite wait behind that check.
- A reconnect starts a fresh render, so read current state on every run and start durable jobs somewhere else.
- `connected_untracked(cx)` reads the flag without requesting a connection.
- One WebSocket serves a tab. The server allows 64 connected renders per socket by default
  (`.max_runs_per_connection(n)`); extra requests get 429.