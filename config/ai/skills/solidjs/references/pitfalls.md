# Pitfalls

Each of these happened while building the todo-app Solid client. Symptom first, then the fix.

## Solid 2.0

- **A 1.x pattern compiles but misbehaves.** One-argument `createEffect`, `createResource`, `onMount`,
  path setters (`setStore("user", "name", x)`), destructured props, `<C value={count} />`. Check
  every snippet against the primer (`solid-2.0-primer.md`) before using it.
- **A new value is not there yet.** Reads lag writes: after `setEditing(false)`, `editing()` is still
  `true` until the microtask flush. A guard that must hold *at once* (a blur fired by removing the
  element, a second Enter while saving) uses a plain variable next to the signal (`let open`,
  `let saving`). In tests, `flush()` before asserting.
- **`UNCOMMITTED_INPUT_OVERWRITTEN`.** A `checked`/`value` binding re-ran (another binding in the same
  template changed, e.g. a `disabled` busy flag) and wrote the old value over what the user clicked.
  Bind the control to a value that the edit itself writes: an optimistic signal set in the action
  (`createOptimistic(() => props.todo.completed)`), which also reverts by itself if the save fails.
- **`IMMUTABLE_UPDATE_IN_STORE`.** A store row was replaced with a new object
  (`list[i] = todo`) or the array with a filtered copy. Write through the draft: `Object.assign(list[i],
  todo)`, `splice`, `push`.
- **`STRICT_READ_UNTRACKED`.** A reactive read at the top of a component body or a flow callback
  (`<For>`, `<Show>`). Move the read into JSX, a memo or an effect's compute function; never wrap it
  in `untrack`.
- **Writes throw in dev.** No signal or store writes in a component body, memo or effect compute.
  Write from event handlers, `onSettled`, actions, or async callbacks.
- **`merge(defaults, props)` keeps `undefined`.** In 2.0 `undefined` overrides. For optional props
  with defaults, read `props.x ?? default` where it is used (inside JSX or a compute function).
- **A failed toggle leaves the box ticked.** The click changed the DOM; nothing reverted it. Use the
  optimistic value above, not a manual `checkbox.checked = …`.

## Router

- **"API docs" opens the not-found page.** The router intercepts every same-origin link. A page the
  deployment serves (`/docs/`) needs `target="_self"`.
- **No filter link is current for an unknown value.** The router sets `aria-current` only on the
  link equal to the address. If the page falls back (`/?filter=bogus` shows All), set
  `aria-current={current ? "page" : "false"}` yourself; the router leaves an `aria-current` it did
  not write alone.
- **Filter changes hold the old list.** A navigation is a transition: the address, `aria-current`
  and the new list switch together when the new list has loaded. Wait for `aria-current` in tests.

## Kobalte

- **Clicks on the checkbox time out ("intercepts pointer events").** See `kobalte-tailwind.md`: lay
  the native input invisibly over the drawn box.
- **`Parameter 'event' implicitly has an 'any' type`.** Handlers on polymorphic parts are not
  inferred; annotate the event (`FocusEvent & { currentTarget: HTMLInputElement }`).
- **Version drift.** `@kobalte/core@latest` is the Solid 1 line. Install `@alpha`, pin exactly.

## Build, dev server, PWA

- **`504 (Outdated Optimize Dep)` and a blank page in dev after moving files.** Vite's dependency
  cache is stale: delete `node_modules/.vite` or run `bun run dev --force`.
- **`Cannot find name 'process'` in `vite.config.ts`.** Add `@types/node` and `"node"` to
  `compilerOptions.types`.
- **The API URL is wrong in the image.** It is baked in at build time. Pass it as a build argument
  (`ARG TODO_API_URL=""` + `ENV`) and remember that empty and unset differ.
- **Every rebuild shows "new version available".** The shell's precache revision was the build time.
  Hash it from content (`manifestTransforms`, see `pwa.md`).
- **A navigation to `/health` or `/api` returns the app.** The worker's navigation fallback caught
  it. Deny those paths bare and with a sub-path: `/^\/health(\/|$)/`.
- **No service worker in dev.** By design; test PWA behaviour on a production build.
- **Source edits never show in the dev server; the page is an old build.** A worker from an earlier
  production build on the same address is serving it, and could not update because the dev server
  answered `/sw.js` with HTML. Serve a retiring worker at `/sw.js` in dev (`pwa.md`).
- **A host keeps `sw.js` or `index.html` cached.** Register with `updateViaCache: 'none'` and send
  `Cache-Control: no-cache` for everything that is not content-hashed (`pwa.md`, Hosting).
- **WebKit page loads fail under `context.setOffline(true)` in Playwright.** Stop the test server
  instead to simulate no network.
- **An installed app on a phone never updates.** The browser only checks on a page load, and the
  app is resumed, not reloaded. Check on `visibilitychange` and on an interval
  (`SystemUpdatePrompt`).
