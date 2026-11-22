"""Rewrites project-setup.md, backend-api.md and pwa.md from the todo-app Solid client, so the code in
them stays verbatim. Run after changing the client:

    python3 ~/.claude/skills/solidjs/scripts/sync-references.py <todo-app>/frontend/solid

Prose lives here (project-setup, backend-api) and in pwa.md.in next to this script.
"""
import pathlib
import re
import sys

app = pathlib.Path(sys.argv[1])
out = pathlib.Path(__file__).resolve().parent.parent / 'references'

LANG = {'.ts': 'ts', '.tsx': 'tsx', '.json': 'jsonc', '.css': 'css', '': 'dockerfile'}
NAMED = {'_headers': 'text'}


def block(rel: str, title: str | None = None) -> str:
    path = app / rel
    lang = NAMED.get(path.name) or LANG.get(path.suffix, 'text')
    body = path.read_text().rstrip('\n')
    return f"### `{title or rel}`\n\n```{lang}\n{body}\n```\n"


def caddyfile() -> str:
    compose = (app / '../../compose.yaml').read_text()
    match = re.search(r"  caddyfile:\n    content: \|\n((?:      .*\n|\n)+)", compose)
    lines = [line[6:] for line in match.group(1).splitlines()]
    return '```caddyfile\n' + '\n'.join(lines).rstrip() + '\n```\n'


setup = f"""# Project setup

Every file below is copied from the todo-app Solid client (`frontend/solid/`). Rename the todo
parts for a new app; keep the rest. A new project can start from the Solid CLI's
`with-tailwindcss` template (it brings start mode, file routes, oxlint and `AGENTS.md`), then:
delete its demo routes and components, drop `@solidjs/meta` and the testing-library packages
unless they are used, and add Kobalte and the PWA packages:

```sh
bun add -d vite-plugin-pwa@2.0.0 workbox-window@7.4.1 @vite-pwa/assets-generator @types/node
bun add --exact @kobalte/core@2.0.0-alpha.5
```

Kobalte goes in `dependencies` (it ships in the bundle) and is pinned exactly because it is an
alpha. Keep one lockfile (`bun.lock`); delete a template's `pnpm-lock.yaml`.

## Packages, TypeScript, lint

{block('package.json')}
`tsconfig.json` needs `"jsxImportSource": "@solidjs/web"` (Solid 2 moved JSX types to the
renderer) and `"node"` in `types` because `vite.config.ts` reads `process.env`.

{block('tsconfig.json')}
{block('oxlint.config.ts')}
## Vite

Start mode in client rendering, file routes, Tailwind, the PWA, and the API address as a build-time
constant. `define` replaces `__TODO_API_URL__` in the source with the string, so the built files
carry the address; `process.env.X ?? default` keeps the difference between unset (local backend)
and empty (same origin).

{block('vite.config.ts')}
{block('src/vite-env.d.ts')}
## Shell, router, pages

`Document.tsx` is picked up by convention and prerendered into `dist/client/index.html`; the client
entry script is injected for you. Keep `<HydrationScript />`: it is stripped in client mode and
needed only if the app ever moves to SSR, which this house style does not do.

{block('src/Document.tsx')}
{block('src/router.ts')}
`App.tsx` renders the router instance; its render-prop child is the root layout and receives the
matched page as `props.children`. Site-wide things (header, "API docs" link, update prompt) live
here, not in a page component.

{block('src/App.tsx')}
{block('src/routes/index.tsx')}
{block('src/routes/[...404].tsx')}
## Theme and shared looks

Tailwind 4 is configured in CSS. The tokens are the shadcn/Rust-UI set (Neutral base, Blue accent),
mapped to utilities with `@theme inline`; dark mode follows `prefers-color-scheme`.

{block('src/App.css')}
`ui.ts` holds the class strings shared by several components, written out whole so Tailwind's
scanner finds them, including the parts of Kobalte components (see `kobalte-tailwind.md`).

{block('src/ui.ts')}
{block('src/focus.ts')}
A ref runs before its element is in the document, so focusing waits for `onSettled`. Use it as
`ref={{focusOnMount()}}` on a Kobalte `TextField.Input` or a plain input.

## Container image

Built once on the build machine (the output is the same for every platform), served by Caddy:

{block('Dockerfile')}
On Cloudflare Pages or Netlify, `public/_headers` (copied into the build) does what the Caddy
headers below do:

{block('public/_headers')}
The Caddy configuration (from the todo-app `compose.yaml`) serves the site, answers `/health` for
the container healthcheck, and passes `/api` to the backend so the image can be built with an empty
API URL:

{caddyfile()}"""

backend = f"""# Talking to the backend

The app never renders on a server and has no server functions. It calls a JSON API that someone else
serves, described by an OpenAPI document. Everything that talks to it is in `src/backend/`; state
built from it is in `src/stores/`; components only call those.

## `src/backend/http.ts`: one generic client

- The address comes from the build (`define` in `vite.config.ts`); trailing slashes are trimmed.
- A failed `fetch` (network down, CORS refused) becomes "unreachable"; a non-2xx answer becomes the
  message of its RFC 9457 problem: field errors (`errors`: member → messages) first, then `detail`,
  then `title`, then the status; a 204 returns `undefined`.
- Every failure is an `ApiError` whose message is ready to show. `errorMessage(e)` turns anything
  caught into such a message.

{block('src/backend/http.ts')}
## `src/backend/<resource>.ts`: the endpoints

Types are written by hand from the OpenAPI document (keep the JSON field names, e.g.
`created_at`), next to small pure helpers that mirror the server's rules (here: unknown filter
values mean `all`, which todos a filter shows).

{block('src/backend/todos.ts')}
## `src/stores/<resource>.ts`: the state

`createStore(fn, seed, {{ key }})` is a **writable** store whose source is async: the function
re-runs when what it reads changes (the filter), and the store can also be written between loads.
Each change writes the API's answer into it:

- an updated row: `Object.assign(list[index], todo)` on the draft, field by field; replacing the
  object (`list[index] = todo`) re-runs every reader of the row and Solid reports
  `IMMUTABLE_UPDATE_IN_STORE`;
- a row that no longer matches the filter: `splice`; a new one: `push`; a removal: `splice`, not
  `filter` (a new array replaces every index).

`attempt` is a generator used inside an action as `yield* todos.attempt(call)`: it clears the last
error, waits for the call, and on failure shows the message and returns `undefined`. Solid's
`action` throws a rejected `yield` back into the generator, so `try/catch` around `yield` works.

{block('src/stores/todos.ts')}
### Why this primitive

| Option | Why not |
|---|---|
| `createMemo(() => listTodos(filter()))` | Read-only: showing a change needs `refresh()`, a GET after every change |
| `createProjection(...)` | Read-only for the same reason |
| `createOptimisticStore(...)` | Its writes revert when the action settles; the API's answer would vanish unless followed by a refetch |
| `createStore(fn, seed, {{ key }})` | Chosen: the source loads, writes apply the API's answer, one request per change |

## Components that use it

The page component creates the store, reads the filter from the address, and shows the list inside
`<Errored>` and `<Loading>`. Retry is `todos.reload()` (`refresh(items)`) then the boundary's
`reset()`. "Adding" is an optimistic flag: set at the start of the action, it falls back to `false`
when the action settles, whatever happened. A failed change shows in an `Alert` above the list.

{block('src/components/TodoList.tsx')}
A row: each change is an action; `busy` is optimistic; the checkbox shows an optimistic
`completed`, so a refused change puts it back by itself. The rename box keeps plain `open`/`saving`
variables next to the signal because reads lag writes: removing the box blurs it, which calls `save`
again before `editing()` has changed.

{block('src/components/TodoItem.tsx')}
## Tests

Unit tests cover the plain TypeScript (problem messages, the network failure, filters); behaviour
in the browser is tested end to end with Playwright against a running backend.

{block('src/backend/http.test.ts')}
{block('src/backend/todos.test.ts')}"""

(out / 'project-setup.md').write_text(setup)
(out / 'backend-api.md').write_text(backend)

pwa_parts = {
    'SYSTEM_UPDATE_PROMPT': block('src/components/SystemUpdatePrompt.tsx'),
    'PWA_ASSETS_CONFIG': block('pwa-assets.config.ts'),
}
template = (pathlib.Path(__file__).resolve().parent / 'pwa.md.in').read_text()
for key, value in pwa_parts.items():
    template = template.replace('{{' + key + '}}', value)
(out / 'pwa.md').write_text(template)
print('written', sorted(p.name for p in out.iterdir()))
