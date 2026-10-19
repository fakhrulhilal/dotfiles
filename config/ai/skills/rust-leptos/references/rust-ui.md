# Rust/UI and Tailwind CSS

[Rust/UI](https://www.rust-ui.com) is shadcn/ui for Leptos: a registry of components styled with Tailwind CSS v4 whose
source you copy into your project and own. It is not a crate you depend on. Upstream for Leptos is
<https://github.com/rust-ui/leptos-ui> (the `rust-ui/ui` repository is now the Dioxus edition and pulls the Leptos one in
as a submodule).

Written against the registry at commit `9cb8e9f` (September 2026), `leptos_ui` 0.3.22, `tw_merge` 0.1.22, `icons`
0.19, `ui-cli` 0.3.16, Tailwind 4.3, and checked by building a CSR todo client with Trunk. The registry changes often;
compare a component with upstream before trusting a detail here.

## Where the source is

| Need | Source |
|------|--------|
| Browse components, demos, blocks, hooks | <https://www.rust-ui.com> (`/docs/components`, `/docs/hooks`, `/blocks`) |
| A component's code as it is installed | `public/registry/styles/default/<name>.md` in rust-ui/leptos-ui: front matter (`cargo_dependencies`, `registry_dependencies`) plus the code under "Component Code", with `crate::components::...` import paths |
| The same code in its development form | `app_crates/registry/src/ui/<name>.rs` (imports use `crate::ui::...`) |
| The helper macros | `crates/leptos_ui/src/` (`clx!`, `void!`, `variants!`) |
| The theme `ui init` writes | `crates/ui-cli/src/command_init/template.rs` and `colors.rs` |

Prefer a shallow clone into a scratch folder over the website; the markdown files are complete.

## Two ways to install

**With the CLI** (cargo-leptos projects):

```sh
cargo install ui-cli --locked
ui init              # picks base and accent colours, writes tailwind.css and ui_config.toml, cargo-adds the helper crates
ui add button card   # copies components (and their registry dependencies) into src/components/ui/
ui list | ui search <q> | ui view <name> | ui diff | ui update
ui mcp init --client claude   # exposes the registry to an AI editor over MCP
```

`ui init` finds the Tailwind input file through `tailwind-input-file` in `[package.metadata.leptos]` (cargo-leptos)
and sets up the Tailwind CLI through npm (`@tailwindcss/cli`, `tw-animate-css`).

**By hand** (Trunk/CSR projects, or to avoid Node): copy the "Component Code" block of each
`<name>.md` into `src/components/ui/<name>.rs`, hooks into `src/components/hooks/`, and the toast family into
`src/components/ui/toast_custom/`. Follow `registry_dependencies` (e.g. `item` needs `separator`, `dialog` needs
`button` and the `use_random` hook) and add the `cargo_dependencies`.

```text
src/components/
├── mod.rs        pub mod ui; (pub mod hooks;)
├── ui/
│   ├── mod.rs    #![allow(dead_code)]  then pub mod button; pub mod input; ...
│   ├── button.rs
│   └── ...
└── hooks/        use_random.rs, use_form.rs, use_theme_mode.rs, ...
```

Put a header comment on each copied file naming the registry commit, and keep the files otherwise unchanged
(`cargo fmt` aside) so they can be compared with upstream. `#![allow(dead_code)]` in `ui/mod.rs` silences the unused
variants the components ship with.

## Crates the components need

```toml
leptos = { version = "0.8", features = ["csr"] }        # or hydrate/ssr
leptos_router = "0.8"   # Button with href calls leptos_router::hooks::use_location
leptos_ui = "0.3.22"    # clx!, void!, variants!
tw_merge = { version = "0.1.22", features = ["variant"] }
icons = { version = "0.19.0", features = ["leptos"] }    # Lucide icons: icons::Check, icons::X, ...
strum = { version = "0.28.0", features = ["derive"] }   # Input's InputType and others
# form: serde, validator; date pickers and charts list their own
```

- `leptos_ui` enables Leptos's `nightly` feature. That feature only acts on a nightly compiler (it checks
  `rustc_nightly`), so stable Rust works. The CLI's old nightly warning was wrong and has been removed.
- Edition 2024 is assumed: components use `if ... && let Some(x) = ...` chains (Rust 1.88+).

## The macros

```rust
use leptos_ui::{clx, void, variants};

clx! {CardTitle, h2, "leading-none font-semibold"}    // <h2> with class merging; props: class: String, children
void! {Skeleton, div, "animate-pulse rounded-md bg-muted"}   // self-closing; props: class only

variants! {
    Button {
        base: "inline-flex items-center ...",
        variants: {
            variant: { Default: "bg-primary ...", Outline: "border ...", Ghost: "hover:bg-accent ..." },
            size: { Default: "h-9 px-4", Sm: "h-8 px-3", Icon: "size-9" }
        },
        component: { element: button, support_href: true, support_aria_current: true }
    }
}
```

`variants!` generates `ButtonVariant` and `ButtonSize` enums, a `ButtonClass` struct, and a `Button` component with
`variant`, `size` and `class` props (all `Signal`s, so they can be reactive), `data_name`, `href` (when
`support_href`), and `children`. With `href` it renders `<a>`; with `support_aria_current` it sets
`aria-current="page"` when the current **path** equals or starts with `href`.

Classes are merged with `tw_merge!`: a `class="px-2"` passed in replaces the base `px-4` instead of fighting it.

## Props are mostly plain values

Rust/UI components declare only the props they need, and many are not reactive. Before relying on one, read its
signature:

| Component | Notable props | Reactive? |
|-----------|---------------|-----------|
| `Button` (`variants!`) | `variant`, `size`, `class`, `href` | variant/size/class yes; there is **no** `disabled`, `type` or `on_click` prop |
| `Input` | `r#type: InputType`, `placeholder`, `name`, `id`, `disabled: bool`, `readonly`, `required`, `autofocus`, `minlength`, `min`/`max`/`step`, `bind_value: Option<RwSignal<String>>`, `node_ref` | Only `bind_value`. `disabled: bool` is read once |
| `Checkbox` | `checked: Signal<bool>`, `disabled: Signal<bool>`, `on_checked_change: Option<Callback<bool>>`, `aria_label` | Yes. It is a `<button role="checkbox">`, not an `<input>` |
| `Select` | `default_value`, `on_change: Option<Callback<Option<String>>>` | Open/close is script-driven (below) |
| `SelectNative` | a styled native `<select>` | Use it when the value must be bound from Rust |
| `clx!` parts (`Alert`, `CardContent`, `EmptyTitle`, ...) | `class: String`, `children` | Static class |

Anything missing goes through attribute spreading, which reaches the component's root element:

```rust
view! {
    <Button
        variant=ButtonVariant::Ghost
        size=ButtonSize::Sm
        attr:r#type="submit"
        attr:disabled=move || pending.get()          // reactive attribute
        on:click=move |_| save()                      // DOM event on the root element
    >
        "Save"
    </Button>
    <Input bind_value=title required=true attr:maxlength=200 attr:aria-label="Title"
        on:keydown=on_keydown on:blur=move |_| commit() />
    <Alert class="border-destructive/50 text-destructive" attr:role="alert">
        <AlertDescription>{move || error.get()}</AlertDescription>
    </Alert>
}
```

If a prop must be reactive and spreading cannot reach it (Input's `disabled: bool`), change the copied component:
the code is yours.

## Overlays run on inline scripts

`dialog` (and `alert_dialog`, built on it), `popover`, `select`, `dropdown_menu`, `context_menu`, `menubar`, `navigation_menu`,
`hover_card`, `sheet`, `drawer`, `command`, `multi_select`, `carousel` and `action_bar` open, close and position
themselves with an inline `<script>` that wires `data-*` attributes (`data-dialog-trigger`, `data-state="open"`).

- They work before WASM loads, which suits SSR.
- Their open state is not a signal. Opening a dialog from Rust after an async result means dispatching a click on the
  trigger or changing the component.
- A Content-Security-Policy needs `'unsafe-inline'` scripts, or a hash per component.
- In CSR they are created by Leptos at runtime; test that the script runs before depending on one.

Everything else (button, input, textarea, checkbox, switch, card, alert, badge, empty, skeleton, spinner, table, tabs,
tooltip, accordion, collapsible, ...) is plain Leptos. Check with `grep -l '<script' <name>.md` before adding a component.

Toasts: `toast` / `toaster` / `toast_wrapper` (registry folder `toast_custom`) are signal-driven:
call `provide_toaster()` at the root, render `<Toaster/>`, then `show_toast().error("...")` or
`expect_toaster().toast(ToastBuilder::new(msg).with_level(...))` from any handler. `sonner` instead fires from
`data-toast-*` attributes on a trigger button, so it cannot be driven from an async result.

Forms: `form` wraps a `use_form` hook over a struct with `#[derive(Validate)]` (`validator` crate), which fits a shared
contract crate whose types already validate.

## Tailwind without Node (Trunk)

Trunk compiles Tailwind with the standalone CLI it downloads:

```html
<!-- index.html -->
<link data-trunk rel="tailwind-css" href="tailwind.css" />
<link data-trunk rel="rust" data-bin="app" data-wasm-opt="z" data-cargo-profile-release="wasm-release" />
```

```toml
# Trunk.toml
[tools]
tailwindcss = "4.3.3"   # pin v4; Trunk downloads it on the first build
```

```css
/* tailwind.css */
@import "tailwindcss";
@source "./src";                      /* class names live in .rs files, including tw_merge! strings */

:root {
  --radius: 0.625rem;
  --background: oklch(1 0 0);
  --foreground: oklch(0.145 0 0);
  --primary: oklch(0.488 0.243 264.376);   /* Blue accent; Neutral is oklch(0.205 0 0) */
  --primary-foreground: oklch(0.97 0.014 254.604);
  /* card, popover, secondary, muted, accent, destructive, border, input, ring: copy from colors.rs */
}

@media (prefers-color-scheme: dark) {
  :root { --background: oklch(0.145 0 0); /* ...the dark values */ }
}

@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-primary: var(--primary);
  /* one --color-x: var(--x) per variable, plus --radius-sm/md/lg/xl from --radius */
}

@layer base {
  * { @apply border-border outline-ring/50; }
  body { @apply bg-background text-foreground; }
}
```

- Copy the variable values for a base colour (Neutral, Stone, Zinc, Mauve, Olive, Mist, Taupe) and accent from
  `colors.rs`; `ui init` uses exactly these.
- Dark mode: `ui init` puts dark values under a `.dark` class. Tailwind v4's `dark:` variant follows
  `prefers-color-scheme` unless `@custom-variant dark (&:where(.dark, .dark *));` is declared. Pick one model and make
  the variables and the variant agree: either the media query (as above, no toggle) or the class (with
  `theme_toggle` / `use_theme_mode`, which set `.dark`).
- `tw-animate-css` (animate-in, fade-in, ...) is an npm import; skip it under Trunk unless a used component needs it.
- Components may use colours the base theme does not define (`bg-success`, `bg-warning`, `bg-info`). Unused
  variants are harmless; define the variable before using one.
- cargo-leptos builds Tailwind itself with `tailwind-input-file = "style/tailwind.css"` in `[package.metadata.leptos]`.

## Gotchas met in practice

- `Button href=...` decides `aria-current` from the path only. Links that differ only by query (`/?filter=active`)
  are all "current" or none are. Render them as plain `<a>` with your own
  `aria-current=move || (current.get() == option).then_some("page")` and style `aria-[current=page]:...`.
- The router intercepts same-origin `<a>` clicks. A link to something the router does not own on the same origin
  (`/swagger-ui`, a file) needs `rel="external"`.
- `Checkbox` is a button: tests and selectors should use `role="checkbox"` and `aria-checked`, and the inner
  `<span>` is the check mark, not the label.
- Spread `on:` handlers land on the root element only; for `Input` that is the `<input>`, which is what you want.

## Choosing Rust/UI

Pick it when Tailwind and owning the markup matter, and when an app mostly needs forms, lists, cards and alerts.
Expect to write more glue than with a full component crate: reactive disabled states, programmatic dialogs and value
bindings are sometimes yours to add. Keep components that use inline scripts out of logic-heavy flows, or replace
their script with signals.
