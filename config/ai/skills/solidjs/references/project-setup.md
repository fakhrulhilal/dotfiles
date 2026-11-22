# Project setup

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

### `package.json`

```jsonc
{
  "name": "todo",
  "description": "The todo web client in Solid 2.0, styled with Tailwind CSS",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "serve": "vite preview",
    "typecheck": "tsc",
    "test": "vitest run",
    "lint": "oxlint src",
    "icons": "pwa-assets-generator"
  },
  "devDependencies": {
    "@solidjs/diagnostics": "^2.0.0",
    "@solidjs/start-devtools": "^1.0.0-next.4",
    "@solidjs/vite-plugin": "^3.0.0",
    "@tailwindcss/vite": "^4.3.5",
    "@types/node": "^26.6.6",
    "@vite-pwa/assets-generator": "^2.0.0",
    "eslint-plugin-solid": "~0.20.0",
    "filesystem-routing": "0.5.0",
    "oxlint": "~1.92.0",
    "tailwindcss": "^4.3.5",
    "typescript": "^7.0.2",
    "vite": "^8.3.8",
    "vite-plugin-pwa": "2.0.0",
    "vitest": "^5.1.0"
  },
  "dependencies": {
    "@kobalte/core": "2.0.0-alpha.5",
    "@solidjs/router": "^2.0.0",
    "@solidjs/web": "^2.0.0",
    "solid-js": "^2.0.0",
    "workbox-window": "7.4.1"
  },
  "private": true
}
```

`tsconfig.json` needs `"jsxImportSource": "@solidjs/web"` (Solid 2 moved JSX types to the
renderer) and `"node"` in `types` because `vite.config.ts` reads `process.env`.

### `tsconfig.json`

```jsonc
{
  "compilerOptions": {
    // General
    "jsx": "preserve",
    "jsxImportSource": "@solidjs/web",
    "target": "ESNext",

    // Modules
    "allowSyntheticDefaultImports": true,
    "esModuleInterop": true,
    "isolatedModules": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "noEmit": true,

    // Type Checking & Safety
    "strict": true,
    "skipLibCheck": true,
    "types": ["vite/client", "node"]
  }
}
```

### `oxlint.config.ts`

```ts
import { defineConfig } from 'oxlint';
import solidV2 from 'eslint-plugin-solid/configs/v2';

export default defineConfig({
  jsPlugins: ['eslint-plugin-solid'],
  ignorePatterns: ['**/*.gen.*', 'dist'],
  settings: solidV2.settings,
  rules: solidV2.rules,
});
```

## Vite

Start mode in client rendering, file routes, Tailwind, the PWA, and the API address as a build-time
constant. `define` replaces `__TODO_API_URL__` in the source with the string, so the built files
carry the address; `process.env.X ?? default` keeps the difference between unset (local backend)
and empty (same origin).

### `vite.config.ts`

```ts
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import tailwindcss from '@tailwindcss/vite';
import { fileRoutes } from 'filesystem-routing/vite';
import type { Plugin } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';
import { defineConfig } from 'vitest/config';
import solid from '@solidjs/vite-plugin';

// Where the client finds the API, read when the site is built, as in the
// other clients: unset means the backend on its default port, empty means the
// address the site itself is served from.
const apiUrl = process.env.TODO_API_URL ?? 'http://127.0.0.1:8080';

interface PrecacheEntry {
    url: string;
    revision: string | null;
    size: number;
}

// The shell's revision is derived from its content, not the build time, so a rebuild of
// the same code is not announced to users as a new version.
function addShell(entries: PrecacheEntry[]) {
    const hash = createHash('sha256').update(readFileSync('src/Document.tsx'));
    for (const entry of entries) hash.update(`${entry.url} ${entry.revision ?? ''}\n`);
    const shell = { url: '/index.html', revision: hash.digest('hex').slice(0, 16), size: 0 };
    return { manifest: [...entries, shell] };
}

// The dev server has no service worker, and without this it answers /sw.js with its HTML page.
// A worker that a production build left on the same address would then never be replaced and
// keep serving that old build; this script replaces it, empties its caches and unregisters it.
const RETIRING_SERVICE_WORKER = `self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil((async () => {
    for (const key of await caches.keys()) await caches.delete(key);
    await self.registration.unregister();
    for (const client of await self.clients.matchAll({ type: 'window' })) client.navigate(client.url);
})()));
`;

function retireServiceWorkerInDev(): Plugin {
    return {
        name: 'retire-service-worker-in-dev',
        apply: 'serve',
        configureServer(server) {
            server.middlewares.use('/sw.js', (_request, response) => {
                response.setHeader('content-type', 'text/javascript');
                response.setHeader('cache-control', 'no-store');
                response.end(RETIRING_SERVICE_WORKER);
            });
        },
    };
}

export default defineConfig({
    // Client mode: no index.html and no mount file. The plugin generates the
    // entries around src/App.tsx (wrapped in src/Document.tsx), and `vite build`
    // prerenders the shell into a purely static dist/client.
    plugins: [
        retireServiceWorkerInDev(),
        // `extensions` makes @solidjs/vite-plugin also compile the `?pick=` route
        // modules the fileRoutes plugin emits (their ids end in a query string).
        solid({ start: true, extensions: ['.jsx', '.tsx'], diagnostics: true }),
        fileRoutes({ types: true }),
        tailwindcss(),
        VitePWA({
            // There is no index.html to inject into: src/Document.tsx links the
            // manifest and src/components/SystemUpdatePrompt.tsx registers the worker.
            injectRegister: false,
            // A new version waits until the user chooses to reload (SystemUpdatePrompt).
            registerType: 'prompt',
            manifest: {
                name: 'Todos',
                short_name: 'Todos',
                description: 'A todo list that calls the todo JSON API.',
                start_url: '/',
                scope: '/',
                display: 'standalone',
                background_color: '#ffffff',
                theme_color: '#ffffff',
                icons: [
                    { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
                    { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
                    { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
                    { src: 'maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
                ],
            },
            workbox: {
                globPatterns: ['**/*.{js,css,html,svg,png,ico,webmanifest}'],
                // The shell is prerendered after the client bundle, so the service
                // worker is generated before index.html exists; it is listed here.
                manifestTransforms: [addShell],
                // Paths that are not files are the app's own pages, served from the
                // cached shell, except what the deployment serves itself.
                navigateFallback: '/index.html',
                navigateFallbackDenylist: [/^\/api(\/|$)/, /^\/health(\/|$)/, /^\/docs(\/|$)/],
                cleanupOutdatedCaches: true,
            },
        }),
    ],
    define: {
        __TODO_API_URL__: JSON.stringify(apiUrl),
    },
    server: {
        host: '127.0.0.1',
        port: 8087,
        strictPort: true,
    },
    preview: {
        host: '127.0.0.1',
        port: 8087,
    },
    build: {
        target: 'esnext',
        assetsInlineLimit: 0,
    },
});
```

### `src/vite-env.d.ts`

```ts
/// <reference types="vite/client" />
/// <reference types="../file-routes.d.ts" />

/** TODO_API_URL at build time; see vite.config.ts. */
declare const __TODO_API_URL__: string;
```

## Shell, router, pages

`Document.tsx` is picked up by convention and prerendered into `dist/client/index.html`; the client
entry script is injected for you. Keep `<HydrationScript />`: it is stripped in client mode and
needed only if the app ever moves to SSR, which this house style does not do.

### `src/Document.tsx`

```tsx
import type { ParentProps } from "solid-js";
import { HydrationScript } from "@solidjs/web";

// Found by the plugin's src/Document.* convention and prerendered into dist/client/index.html.
export default function Document(props: ParentProps) {
    return (
        <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta
                    name="viewport"
                    content="width=device-width, initial-scale=1"
                />
                <meta name="color-scheme" content="light dark" />
                <meta
                    name="description"
                    content="A todo list that calls the todo JSON API."
                />
                <meta
                    name="theme-color"
                    content="#ffffff"
                    media="(prefers-color-scheme: light)"
                />
                <meta
                    name="theme-color"
                    content="#0a0a0a"
                    media="(prefers-color-scheme: dark)"
                />
                <title>Todos</title>
                <link rel="icon" href="/favicon.ico" sizes="48x48" />
                <link
                    rel="icon"
                    href="/icon.svg"
                    sizes="any"
                    type="image/svg+xml"
                />
                <link
                    rel="apple-touch-icon"
                    href="/apple-touch-icon-180x180.png"
                />
                {/* Written by vite-plugin-pwa (vite.config.ts) at build time. */}
                <link rel="manifest" href="/manifest.webmanifest" />
                <HydrationScript />
            </head>
            <body>{props.children}</body>
        </html>
    );
}
```

### `src/router.ts`

```ts
import { pageRoutes } from 'virtual:file-routes';
import { createRouter } from '@solidjs/router';
import { fileRoutes } from '@solidjs/router/fs';

export const Router = createRouter({
    routes: fileRoutes(pageRoutes),
});
```

`App.tsx` renders the router instance; its render-prop child is the root layout and receives the
matched page as `props.children`. Site-wide things (header, "API docs" link, update prompt) live
here, not in a page component.

### `src/App.tsx`

```tsx
import { Loading } from "solid-js";
import SystemUpdatePrompt from "./components/SystemUpdatePrompt";
import { Router } from "./router";
import "./App.css";

export default function App() {
    return (
        <Router>
            {(props) => (
                <>
                    <header class="mx-auto flex max-w-xl items-baseline justify-between px-4 pt-8 pb-4">
                        <a href="/" class="text-2xl font-bold tracking-tight">
                            Todo list
                        </a>
                        {
                            /* Served by the deployment (compose.yaml: Caddy passes /docs on).
                target="_self": the router would otherwise treat it as one
                of its own pages. */
                        }
                        <a
                            href="/docs/"
                            target="_self"
                            class="text-sm text-primary underline-offset-4 hover:underline"
                        >
                            API docs
                        </a>
                    </header>
                    <main class="mx-auto max-w-xl px-4 pb-12">
                        <Loading>{props.children}</Loading>
                    </main>
                    <SystemUpdatePrompt />
                </>
            )}
        </Router>
    );
}
```

### `src/routes/index.tsx`

```tsx
import TodoList from "../components/TodoList";

export default function Home() {
    return <TodoList />;
}
```

### `src/routes/[...404].tsx`

```tsx
import * as ui from "../ui";

export default function NotFound() {
    return (
        <section class={[ui.empty, "p-8"]}>
            <h1 class="text-lg font-medium tracking-tight">Not found</h1>
            <p class="text-sm text-muted-foreground">
                That page does not exist.
            </p>
            <a href="/" class={ui.button.outline}>
                Back to the list
            </a>
        </section>
    );
}
```

## Theme and shared looks

Tailwind 4 is configured in CSS. The tokens are the shadcn/Rust-UI set (Neutral base, Blue accent),
mapped to utilities with `@theme inline`; dark mode follows `prefers-color-scheme`.

### `src/App.css`

```css
/* Tailwind CSS v4, compiled by @tailwindcss/vite. The theme is the Leptos
 * client's (Rust/UI's Neutral base and Blue accent), so the two Tailwind
 * clients look the same; dark mode follows the system setting. */
@import 'tailwindcss';
:root {
  --radius: 0.625rem;
  --background: oklch(1 0 0);
  --foreground: oklch(0.145 0 0);
  --card: oklch(1 0 0);
  --card-foreground: oklch(0.145 0 0);
  --popover: oklch(1 0 0);
  --popover-foreground: oklch(0.145 0 0);
  --primary: oklch(0.488 0.243 264.376);
  --primary-foreground: oklch(0.97 0.014 254.604);
  --secondary: oklch(0.967 0.001 286.375);
  --secondary-foreground: oklch(0.21 0.006 285.885);
  --muted: oklch(0.97 0 0);
  --muted-foreground: oklch(0.556 0 0);
  --accent: oklch(0.97 0 0);
  --accent-foreground: oklch(0.205 0 0);
  --destructive: oklch(0.577 0.245 27.325);
  --border: oklch(0.922 0 0);
  --input: oklch(0.922 0 0);
  --ring: oklch(0.708 0 0);
}

@media (prefers-color-scheme: dark) {
  :root {
    --background: oklch(0.145 0 0);
    --foreground: oklch(0.985 0 0);
    --card: oklch(0.205 0 0);
    --card-foreground: oklch(0.985 0 0);
    --popover: oklch(0.205 0 0);
    --popover-foreground: oklch(0.985 0 0);
    --primary: oklch(0.623 0.214 259.815);
    --primary-foreground: oklch(0.97 0.014 254.604);
    --secondary: oklch(0.274 0.006 286.033);
    --secondary-foreground: oklch(0.985 0 0);
    --muted: oklch(0.269 0 0);
    --muted-foreground: oklch(0.708 0 0);
    --accent: oklch(0.371 0 0);
    --accent-foreground: oklch(0.985 0 0);
    --destructive: oklch(0.704 0.191 22.216);
    --border: oklch(1 0 0 / 10%);
    --input: oklch(1 0 0 / 15%);
    --ring: oklch(0.556 0 0);
  }
}

@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  --color-popover: var(--popover);
  --color-popover-foreground: var(--popover-foreground);
  --color-primary: var(--primary);
  --color-primary-foreground: var(--primary-foreground);
  --color-secondary: var(--secondary);
  --color-secondary-foreground: var(--secondary-foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-accent: var(--accent);
  --color-accent-foreground: var(--accent-foreground);
  --color-destructive: var(--destructive);
  --color-border: var(--border);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --radius-sm: calc(var(--radius) - 4px);
  --radius-md: calc(var(--radius) - 2px);
  --radius-lg: var(--radius);
  --radius-xl: calc(var(--radius) + 4px);
}

@layer base {
  * {
    @apply border-border outline-ring/50;
  }
  body {
    @apply bg-background text-foreground antialiased;
  }
  button:not(:disabled),
  [role="button"]:not(:disabled) {
    cursor: pointer;
  }
}
```

`ui.ts` holds the class strings shared by several components, written out whole so Tailwind's
scanner finds them, including the parts of Kobalte components (see `kobalte-tailwind.md`).

### `src/ui.ts`

```ts
// The looks of the Leptos client's Rust/UI components, as Tailwind classes, so
// the two Tailwind clients look the same. Each string is written out whole:
// Tailwind only generates the classes it finds in the sources.

const buttonBase =
    'inline-flex shrink-0 items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-all outline-none select-none touch-manipulation focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50 active:scale-[0.98]';

export const button = {
    primary: `${buttonBase} h-9 px-4 py-2 bg-primary text-primary-foreground shadow-xs hover:bg-primary/90`,
    outline: `${buttonBase} h-8 gap-1.5 px-3 border bg-background shadow-xs hover:bg-accent hover:text-accent-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50`,
    ghost: `${buttonBase} h-8 gap-1.5 px-3 hover:bg-accent hover:text-accent-foreground dark:hover:bg-accent/50`,
    danger: `${buttonBase} h-8 gap-1.5 px-3 text-destructive hover:bg-accent dark:hover:bg-accent/50`,
};

export const input =
    'flex h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs transition-[color,box-shadow] outline-none selection:bg-primary selection:text-primary-foreground placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 md:text-sm dark:bg-input/30';

// Parts of Kobalte's Checkbox; Kobalte marks the state with data-checked and data-disabled.
export const checkbox = {
    root: 'relative inline-flex shrink-0 items-center',
    control:
        'flex size-4 cursor-pointer items-center justify-center rounded-[4px] border border-input text-primary-foreground shadow-xs transition-shadow dark:bg-input/30 data-checked:border-primary data-checked:bg-primary data-disabled:cursor-not-allowed data-disabled:opacity-50 [input:focus-visible+&]:ring-[3px] [input:focus-visible+&]:ring-ring/50',
    indicator: 'size-3.5',
    // Kobalte hides the native input under the drawn box, where clicks land on the box instead.
    // Stretched invisibly over it, the input takes clicks, focus and the screen reader itself.
    // Inline, because Kobalte sets the hiding styles inline.
    inputStyle: {
        position: 'absolute',
        inset: '0',
        width: '100%',
        height: '100%',
        margin: '0',
        clip: 'auto',
        'clip-path': 'none',
        opacity: '0',
        cursor: 'inherit',
        'z-index': '1',
    } as const,
};

export const card = 'flex flex-col gap-5 rounded-xl border bg-card p-5 text-card-foreground shadow-sm';

export const alert = 'relative w-full rounded-lg border border-destructive/50 px-4 py-3 text-sm text-destructive';

export const skeleton = 'animate-pulse rounded-md bg-muted';

export const empty = 'flex flex-col items-center justify-center gap-4 rounded-lg border border-dashed p-6 text-center';
```

### `src/focus.ts`

```ts
import { onSettled } from 'solid-js';

export function focusOnMount() {
    return onceMounted((input) => input.focus());
}

export function selectOnMount() {
    return onceMounted((input) => {
        input.focus();
        input.select();
    });
}

// A ref runs before its element is attached, too early to focus it.
function onceMounted(apply: (input: HTMLInputElement) => void) {
    let input: HTMLInputElement | undefined;
    onSettled(() => {
        if (input) apply(input);
    });
    return (element: HTMLInputElement) => {
        input = element;
    };
}
```

A ref runs before its element is in the document, so focusing waits for `onSettled`. Use it as
`ref={focusOnMount()}` on a Kobalte `TextField.Input` or a plain input.

## Container image

Built once on the build machine (the output is the same for every platform), served by Caddy:

### `Dockerfile`

```dockerfile
# syntax=docker/dockerfile:1

# The Solid web client, served by Caddy. The site is JavaScript and static
# files, the same for every platform, so it is built once, on the build
# machine's own architecture. Caddy's configuration is supplied by compose.yaml.

ARG BUN_VERSION=1.4.5

FROM --platform=$BUILDPLATFORM oven/bun:${BUN_VERSION} AS build
WORKDIR /src
COPY frontend/solid/package.json frontend/solid/bun.lock ./
RUN --mount=type=cache,target=/root/.bun/install/cache,id=todo-solid-bun,sharing=locked \
    bun install --frozen-lockfile
COPY frontend/solid ./

# Where the client finds the API. Empty: at the address the site itself is
# served from, where Caddy passes API calls on.
ARG TODO_API_URL=""
ENV TODO_API_URL=${TODO_API_URL}
RUN bun run build

FROM caddy:2-alpine
COPY --from=build /src/dist/client /srv
```

On Cloudflare Pages or Netlify, `public/_headers` (copied into the build) does what the Caddy
headers below do:

### `public/_headers`

```text
# Read by Cloudflare Pages and Netlify. Every file is checked with the server before use, so the
# service worker and index.html of a new deploy are never served from a stale copy. (Hashed files
# under /assets/ would also be safe to cache forever, but both hosts merge the headers of every
# matching rule, and caching them is the service worker's job anyway.)
/*
  Cache-Control: no-cache
```

The Caddy configuration (from the todo-app `compose.yaml`) serves the site, answers `/health` for
the container healthcheck, and passes `/api` to the backend so the image can be built with an empty
API URL:

```caddyfile
:80 {
	encode zstd gzip

	# The site's own liveness probe: static, so it answers while Caddy
	# runs, whatever the backend does. One word, as the backends' probes.
	# /health/live and /health/ready go on to the active backend below.
	handle /health {
		header Content-Type "text/plain; charset=utf-8"
		header Cache-Control no-store
		respond Healthy 200
	}

	@api path /api/* /health/*
	handle @api {
		reverse_proxy api:8080
	}

	# The API reference (backend-docs) under /docs. It serves from its own
	# root, so the prefix is stripped, and the two files its page loads by
	# absolute path are passed on as they are.
	redir /docs /docs/
	handle_path /docs/* {
		reverse_proxy backend-docs:8080
	}
	@docs path /configuration.json /openapi/*
	handle @docs {
		reverse_proxy backend-docs:8080
	}

	handle {
		root * /srv
		# Names under /assets/ carry a hash of their content, so a copy never goes stale.
		# Everything else, the service worker and index.html above all, is checked with
		# the server each time, so a new deploy is seen whatever the browser's cache does.
		@hashed path /assets/*
		header @hashed Cache-Control "public, max-age=31536000, immutable"
		@unhashed not path /assets/*
		header @unhashed Cache-Control "no-cache"
		# Paths that are not files belong to the client's router.
		try_files {path} /index.html
		file_server
	}
}
```
