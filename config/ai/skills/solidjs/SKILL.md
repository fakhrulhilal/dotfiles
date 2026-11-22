---
name: solidjs
description: "House style for Solid 2.0 web front ends (SolidJS v2 only, never 1.x): a client-rendered static SPA built with Vite 8 and @solidjs/vite-plugin start mode, file routes with @solidjs/router 2.0 (createRouter, useSearchParams, plain <a> links), Tailwind CSS 4 with Kobalte 2.0 headless components, a JSON API backend reached through src/backend/ (fetch, RFC 9457 problem details, a build-time API URL), state in src/stores/ (createStore with an async source, actions, optimistic flags, <Loading>/<Errored>), and an installable offline PWA with vite-plugin-pwa and an update prompt — with no server functions and no SSR. Use when starting a Solid project, or writing, reviewing or debugging code that imports solid-js, @solidjs/web, @solidjs/router or @kobalte/core (createSignal, createStore, createEffect, action, createOptimistic, Loading, Errored, For, Show, createRouter, Checkbox, TextField), when adding a page, component, store or API call to a Solid app, setting up Tailwind, Kobalte or PWA install/offline/update in Solid, or fixing Solid dev diagnostics such as STRICT_READ_UNTRACKED, UNCOMMITTED_INPUT_OVERWRITTEN or IMMUTABLE_UPDATE_IN_STORE."
license: MIT
---

# Solid 2.0

A Solid app here is a **static single-page app**: Vite builds HTML, JS and CSS once, any static host
serves them, and every read and write goes to a separate JSON API. It installs as a PWA and opens
offline. The reference implementation is the todo-app client, `frontend/solid/` in the todo-app
repository; this skill and that client are kept in step, so when one changes, change the other.
The code in `references/project-setup.md`, `backend-api.md` and `pwa.md` is copied from the client
by `python3 scripts/sync-references.py <todo-app>/frontend/solid`; run it after changing the client.

## Rules

1. **Solid 2.0 only.** `solid-js` 2.x, `@solidjs/web`, `@solidjs/router` 2.x, Kobalte `2.0.0-alpha.x`.
   Never write, suggest or copy 1.x code (`solid-js/web`, `solid-js/store`, `createResource`,
   `onMount`, one-argument `createEffect`, `<Suspense>`, `<Index>`, `classList`, `use:`, router
   `<A>`/`<Route>` components, Kobalte `0.13.x`). Most Solid examples online, and most model priors,
   are 1.x: distrust them. If a project is still on 1.x, say so and stop; don't mix the two.
2. **Read the primer first.** [references/solid-2.0-primer.md](references/solid-2.0-primer.md) is the
   Solid team's agent primer (the `AGENTS.md` that ships with the Solid 2.0 templates, copied from the
   todo-app client). Read it before writing Solid code. The full reference is
   `node_modules/solid-js/CHEATSHEET.md`; diagnostics are explained per code in
   `node_modules/solid-js/skills/reactivity-diagnostics/SKILL.md`. Fix the cause of a diagnostic;
   never silence it (`untrack`, `ownedWrite`).
3. **Client only. Never server functions.** No `"use server"`, no `serverFunctions` option, no
   `ssr: true`, no `src/entry-server.*`, no router server/data APIs (`query`, `formAction`,
   `liveQuery`, `serverRouteComponent`, single-flight mutations). The backend is someone else's JSON
   API, reached with `fetch` from `src/backend/`. `vite build` must produce a plain static
   `dist/client`.
4. **Components follow the domain.** One component per domain record (`TodoItem`) and one per page
   that serves the records (`TodoList`, which owns its form, filters, footer and loading/error
   states). No generic sub-components (`FilterLinks`, `ListFooter`, `AddForm`); name every component
   after the domain it belongs to. Route files stay thin.

## Versions that work together

| Package | Version | Notes |
|---|---|---|
| `solid-js`, `@solidjs/web` | 2.0.0 | `@solidjs/web` owns JSX types: `"jsxImportSource": "@solidjs/web"` |
| `@solidjs/router` | 2.0.0 | `createRouter`, file routes from `@solidjs/router/fs` |
| `@solidjs/vite-plugin` | 3.0.0 | `solid({ start: true })`: client mode, prerendered shell |
| `filesystem-routing` | 0.5.0 | `fileRoutes({ types: true })` Vite plugin, writes `file-routes.d.ts` |
| `@kobalte/core` | `2.0.0-alpha.5`, exact | Solid 2 line (`alpha` tag); `0.13.x` is Solid 1 only |
| `vite` | 8.3 | `vitest/config`'s `defineConfig` |
| `tailwindcss`, `@tailwindcss/vite` | 4.3 | Configured in CSS; not `@kobalte/tailwindcss` (Tailwind 3 only) |
| `vite-plugin-pwa`, `workbox-window` | 2.0.0, 7.4.1 | `generateSW`, `registerType: 'prompt'` |
| `@vite-pwa/assets-generator` | 2.0.0 | Icons from one SVG |
| `typescript` | 7.0 | `tsc` for type checks only (`noEmit`) |
| `oxlint`, `eslint-plugin-solid` | 1.92, 0.20 | Solid's `v2` rule set |
| `vitest` | 5.1 | Unit tests of plain TS (API client, filters) |
| Bun | 1.4 | Package manager and script runner (`bun.lock`) |

Pin Kobalte exactly: it is an alpha and its authors say the v2 components are not fully tested.

## Folder structure

```text
<app>/
  package.json  bun.lock  tsconfig.json  vite.config.ts  oxlint.config.ts  pwa-assets.config.ts
  AGENTS.md                 the Solid 2.0 primer (from the template; keep it)
  Dockerfile                bun build stage, then caddy serving dist/client
  public/                   icon.svg and the icons generated from it
  src/                      ONLY framework files and app-wide helpers:
    App.tsx                 router, site header, page frame, SystemUpdatePrompt
    App.css                 Tailwind import and theme tokens
    Document.tsx            the HTML shell (found by convention, prerendered)
    router.ts               createRouter(fileRoutes(pageRoutes))
    vite-env.d.ts           vite, vite-plugin-pwa and file-route types, build-time constants
    ui.ts                   Tailwind class strings for Kobalte parts (button, input, checkbox, …)
    focus.ts                refs that focus an input once mounted
    routes/                 pages by file name (router convention); thin: render one page component
      index.tsx             <TodoList />
      [...404].tsx          not found
    backend/                everything that talks to the API
      http.ts               API URL, send(), ApiError, problem details → message
      todos.ts              typed endpoints and types from the OpenAPI document
      *.test.ts             vitest, next to what they test
    stores/                 client state per resource
      todos.ts              createTodos(filter): async store + change helpers
    components/             domain components
      TodoList.tsx  TodoItem.tsx  SystemUpdatePrompt.tsx
```

If a file in `src/` is about the business (todos, users, orders), it belongs in a folder. `src/routes/`
must stay where the router plugin looks.

## Stack and setup

- **Build:** Vite with `@solidjs/vite-plugin` in start mode, client rendering:
  `solid({ start: true, extensions: ['.jsx', '.tsx'], diagnostics: true })`. There is no
  `index.html` and no mount file: the plugin wraps `src/App.tsx` in `src/Document.tsx` and prerenders
  the shell into `dist/client/index.html`.
- **Routing:** `fileRoutes({ types: true })` scans `src/routes/`; `createRouter({ routes:
  fileRoutes(pageRoutes) })` makes the router instance, rendered in `App.tsx` with a render-prop child
  (the root layout). Links are plain `<a href>`; the router intercepts same-origin clicks and sets
  `aria-current="page"` when path **and query** match. Filters and other view state go in the query
  string (`/?filter=active`, read with `useSearchParams()`), so reload, bookmarks and Back work on a
  static host. A link the router must not handle (a page the deployment serves, like `/docs/`) gets
  `target="_self"`.
- **Styling:** Tailwind 4 through `@tailwindcss/vite`; the theme is CSS variables in `App.css`
  (`@theme inline`), dark mode follows the system. Class strings live whole in the source (Tailwind
  only sees complete strings); shared looks are constants in `src/ui.ts`. Conditional classes use the
  array/object form: `class={["base", { "line-through": done() }]}`, never string building.
- **Components:** Kobalte 2.0 headless parts (`Button`, `TextField`, `Checkbox`, `Alert`,
  `Skeleton`, …) imported per path (`@kobalte/core/checkbox`), styled with Tailwind; their state is
  in `data-*` attributes (`data-checked:bg-primary`, `data-disabled:opacity-50`).
  [references/kobalte-tailwind.md](references/kobalte-tailwind.md) has the parts, the checkbox fix and
  the alpha's traps.
- **Checks:** `bun run typecheck` (tsc), `bun run lint` (oxlint with `eslint-plugin-solid/configs/v2`),
  `bun run test` (vitest), `bun run build`.

Full files (package.json, vite.config.ts, tsconfig, Document, App, router, theme, ui.ts, Dockerfile):
[references/project-setup.md](references/project-setup.md).

## Talking to the backend

- **Where:** all of it in `src/backend/`. `http.ts` is generic: `send<T>(method, path, body?)`
  around `fetch`, `ApiError` whose message is ready for the user, and `problemMessage()` that turns an
  RFC 9457 problem (`errors` map, `detail`, `title`) into one sentence. `<resource>.ts` has the types
  (written from the API's OpenAPI document) and one function per endpoint.
- **Address:** fixed at build time. `vite.config.ts` reads `process.env.TODO_API_URL` (or the app's
  own variable) and `define`s it as a constant: unset → the local backend's address, empty → the
  site's own origin (behind a reverse proxy that passes `/api` on). Never read it at runtime.
- **State:** `src/stores/<resource>.ts`. The list is a writable store whose source is the API:
  `createStore(() => listTodos(filter()), [], { key: "id" })`. A new filter refetches; each change
  writes the API's answer into the store (`Object.assign` on the draft row, `push`, `splice`), so no
  refetch follows a change. Not `createMemo` (read-only, needs a refetch per change) and not
  `createOptimisticStore` (its writes revert when the action settles).
- **Changes:** each is an `action(function* () { … yield* todos.attempt(call) … })`; `attempt` shows
  the failure message and returns `undefined`. "Busy" flags are `createOptimistic(false)`: set at the
  start of the action, back to `false` by themselves when it settles. A control's own optimistic
  value (`createOptimistic(() => props.todo.completed)`) puts it back when the API refuses.
- **Loading and errors:** the list renders inside `<Errored fallback={(err, reset) => …}>` (message
  plus Retry: `refresh(store)` then `reset()`) and `<Loading fallback={skeleton}>`. Change errors are
  a signal shown in an `Alert`.

Code: [references/backend-api.md](references/backend-api.md).

## PWA

- `vite-plugin-pwa` with `injectRegister: false` (start mode has no `index.html`), `registerType:
  'prompt'`, a manifest with 192/512/maskable icons, Workbox precaching the build, and
  `navigateFallback: '/index.html'` with a denylist for paths the deployment serves (`/api`,
  `/health`, `/docs`).
- `index.html` is written after the worker, so it is added through `manifestTransforms`, with a
  revision hashed from the other files and `src/Document.tsx` (never `Date.now()`: every rebuild would
  look like an update).
- `Document.tsx` links the manifest, icons and `theme-color`; `components/SystemUpdatePrompt.tsx`
  registers the worker with `workbox-window` and `updateViaCache: 'none'`, checks for a new version
  on resume, on coming back online and periodically (`checkIntervalMinutes`, default 60;
  `minCheckGapMinutes`, default 1) and shows a banner with Reload/Later.
- A new deploy must be found whatever the browser or host caches (Safari and iOS are the main
  target): hosts send `Cache-Control: no-cache` for everything not content-hashed (Caddy matchers,
  `public/_headers`), and the dev server answers `/sw.js` with a self-removing worker so an old
  production worker never hides it.
- The API is never cached: offline, the shell opens and the store's error says the service is
  unreachable.

Config, the prompt component, icons, hosting and how to test updates: [references/pwa.md](references/pwa.md).

## Verify a change

1. `bun run typecheck && bun run lint && bun run test && bun run build`.
2. Run `bun run dev` against a real backend and drive the page (Playwright by role and label, never
   CSS class); the dev console must show **no** `[CODE]` diagnostics. A Vite `504 (Outdated Optimize
   Dep)` after moving files is a stale cache: delete `node_modules/.vite` or run `bun run dev --force`.
3. For PWA changes, run the PWA suite (`npm run test:pwa` in the todo-app's `tests/`: WebKit as
   Safari and iPhone, plus Chromium, against a host that caches everything for a day), or test
   `bun run build && bun run serve` by hand (the dev server has no service worker).

Traps met while building the reference client: [references/pitfalls.md](references/pitfalls.md).
