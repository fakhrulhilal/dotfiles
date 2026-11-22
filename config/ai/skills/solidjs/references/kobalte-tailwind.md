# Kobalte with Tailwind

[Kobalte](https://kobalte.dev) is a headless, accessible component library for Solid. Its Solid 2.0
line is `2.0.0-alpha.x` (npm tag `alpha`); `0.13.x` is Solid 1 only and must not be used. The v2 API
is the 0.13 API with Solid 2's own changes, so kobalte.dev's documentation applies, but its
authors say the v2 components are not fully tested: pin one exact version and test every component
you add in the browser.

```sh
bun add --exact @kobalte/core@2.0.0-alpha.5
```

`@kobalte/tailwindcss` is a Tailwind 3 plugin (peer `tailwindcss ^3`); don't install it. Tailwind 4
targets Kobalte's state attributes directly.

## Using a component

Import each component from its own path; parts hang off the component:

```tsx
import { Alert } from "@kobalte/core/alert";
import { Button } from "@kobalte/core/button";
import { Checkbox } from "@kobalte/core/checkbox";
import { Skeleton } from "@kobalte/core/skeleton";
import { TextField } from "@kobalte/core/text-field";
```

Kobalte renders no styles. Classes go on each part; the shared ones are constants in `src/ui.ts`
(full file in `project-setup.md`). State is exposed as attributes on the parts, and Tailwind 4 has a
variant for each:

| Attribute | Tailwind 4 | Set when |
|---|---|---|
| `data-checked` | `data-checked:bg-primary` | checkbox, switch, toggle on |
| `data-disabled` | `data-disabled:opacity-50` | `disabled` |
| `data-invalid` | `data-invalid:border-destructive` | `validationState="invalid"` |
| `data-required`, `data-readonly` | `data-required:…` | `required`, `readOnly` |
| `data-expanded`, `data-highlighted` | `data-expanded:…` | popovers, menus, listbox items |

Keep complete class strings in the source (Tailwind only finds whole strings), and use Solid's
array/object `class` form to add per-use classes: `class={[ui.input, "h-8"]}`.

## The parts the todo client uses

**Button.** Renders `<button type="button">` unless told otherwise (so it never submits a form by
accident) and sets `data-disabled`. A submit button says so: `<Button type="submit">`.

```tsx
<Button class={ui.button.ghost} disabled={busy()} onClick={startEditing}>
    Edit
</Button>
```

**TextField.** `Root` holds the field's props (`name`, `required`, `defaultValue`/`value`,
`onChange`); `Input` is the `<input>`, where `ref`, `class`, `placeholder`, `aria-label`,
`maxlength` and event handlers go. The root listens for its form's reset and goes back to
`defaultValue`, so `form.reset()` after a submit clears Kobalte's state too, and
`new FormData(form).get("title")` reads the value through the input's `name`.

```tsx
<form class="flex gap-2" onSubmit={(event) => { event.preventDefault(); add(event.currentTarget); }}>
    <TextField class="flex-1" name="title" required>
        <TextField.Input
            ref={focusOnMount()}
            class={ui.input}
            placeholder="What needs doing?"
            aria-label="New todo"
            maxlength={MAX_TITLE_CHARS}
        />
    </TextField>
    <Button type="submit" class={ui.button.primary} disabled={adding()}>
        Add
    </Button>
</form>
```

An edit box that is read on Enter or blur stays uncontrolled: `defaultValue` from the record, the
value read from the event. Event handlers on the polymorphic `Input` are not inferred; type them:

```tsx
<TextField class="flex-1" defaultValue={props.todo.title}>
    <TextField.Input
        ref={selectOnMount()}
        class={[ui.input, "h-8"]}
        aria-label="Title"
        onKeyDown={onKeyDown}
        onBlur={(event: FocusEvent & { currentTarget: HTMLInputElement }) =>
            save(event.currentTarget.value)}
    />
</TextField>
```

**Checkbox.** Controlled with an optimistic value, so a change the API refuses puts the box back:

```tsx
<Checkbox
    class={ui.checkbox.root}
    checked={completed()}
    disabled={busy()}
    onChange={(checked) => toggle(checked)}
>
    <Checkbox.Input aria-label="Done" style={ui.checkbox.inputStyle} />
    <Checkbox.Control class={ui.checkbox.control}>
        <Checkbox.Indicator>
            <svg class={ui.checkbox.indicator} viewBox="0 0 24 24" fill="none"
                stroke="currentColor" stroke-width="3" stroke-linecap="round"
                stroke-linejoin="round" aria-hidden="true">
                <path d="M20 6 9 17l-5-5" />
            </svg>
        </Checkbox.Indicator>
    </Checkbox.Control>
</Checkbox>
```

Kobalte hides the native `Input` (1px, clipped) under `Control`, a `<div>` that toggles on click.
A click aimed at the labelled checkbox (a test's `getByLabel('Done').click()`, assistive tech)
lands on the `<div>` and never reaches the input; Playwright reports "intercepts pointer events".
Don't wrap the control in `Checkbox.Label` to fix it: the label activates the input **and** the
control toggles, twice. Instead stretch the input invisibly over the drawn box; it then takes
clicks, focus and the screen reader itself, and the control is only the picture. The style must be
inline (Kobalte sets its hiding styles inline, and merges yours after them), and the root must be
`relative`:

```ts
export const checkbox = {
    root: 'relative inline-flex shrink-0 items-center',
    control:
        'flex size-4 cursor-pointer items-center justify-center rounded-[4px] border border-input text-primary-foreground shadow-xs transition-shadow dark:bg-input/30 data-checked:border-primary data-checked:bg-primary data-disabled:cursor-not-allowed data-disabled:opacity-50 [input:focus-visible+&]:ring-[3px] [input:focus-visible+&]:ring-ring/50',
    indicator: 'size-3.5',
    inputStyle: {
        position: 'absolute', inset: '0', width: '100%', height: '100%', margin: '0',
        clip: 'auto', 'clip-path': 'none', opacity: '0', cursor: 'inherit', 'z-index': '1',
    } as const,
};
```

`[input:focus-visible+&]` draws the focus ring on the control when the input before it has keyboard
focus. `as const` keeps `position` a literal for Solid's `CSSProperties`.

**Alert.** `<Alert class={ui.alert}>message</Alert>` renders a `div` with `role="alert"`.

**Skeleton.** `<Skeleton class={[ui.skeleton, "h-9 w-full"]} />` for loading placeholders; put
`aria-busy="true"` and an `aria-label` on their container.

## What stays plain

- **Links.** Use `<a href>`: the router claims anchors and sets `aria-current` on them. Kobalte's
  `Link` adds nothing for in-app navigation.
- **A component Kobalte doesn't need to own.** Prefer plain elements over a Kobalte part that only
  adds markup.

## Checking a new Kobalte component

1. Typecheck (event types on polymorphic parts often need annotations).
2. In the dev server: drive it with the mouse **and** the keyboard (Tab, Space, Enter, Escape),
   check its role and accessible name in Playwright (`getByRole`, `getByLabel`), and read the console
   for Solid `[CODE]` diagnostics.
3. Look at it in light and dark mode.
