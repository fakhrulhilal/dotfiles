---
name: vue
description: House style for Vue 2.7 / Vue 3 Composition API admin apps in JavaScript + JSDoc - `<script setup>` components, Pinia setup stores, provider/consumer composables with Symbol injection keys, a repository layer over a base HTTP client, dictionary-based i18n, licence/feature gating, analytics through one tracker composable, infinite loading, class-based entity/list models, CSS-variable theming (dark mode), BEM-scoped SCSS and airbnb + prettier lint, plus a playbook for delivering features and fixing bugs. Use when writing, reviewing, refactoring or debugging any `.vue` file, composable, Pinia store, API repository, entity/list class, route guard or tracker in a Vue codebase, when adding a widget, feature, panel, theme/dark-mode change or tracking event, when fixing a front-end bug, or when the user says "the usual Vue way".
---

# Vue house style

Conventions for a large Vue admin SPA where new code is Composition API and older code is
Options API. **New code follows this style; older Options-API code is left as it is and only
bridged to.** How to sequence a feature and fix a bug is in **Delivering a feature**,
**Fixing a bug** and **Known traps** below.

Names in this skill (`useDict`, `useEventTracker`, `BaseRepository`, `BaseList`...) are roles.
If the project already has a helper for the role, use it under its real name; create one with
the shape shown only when nothing exists. Copy-ready templates live in
[references/patterns.md](references/patterns.md). Read it before you create a component,
composable, store, repository function or tracker.

## Stack (don't "upgrade" it)

- **Vue 2.7** (Composition API built in) or Vue 3, plain **JavaScript with JSDoc** - no TypeScript unless the project already uses it.
  Import Composition API from `'vue'` (`ref`, `computed`, `watch`, `provide`, `inject`, `readonly`, `toRef`, `nextTick`...).
- **Pinia** for new state. If **Vuex** still owns parts (account, app, routes), read it - don't migrate it as a side effect.
- A component kit (bootstrap-vue or similar) plus an in-house `S*`-style primitive kit; luxon for dates, lodash, axios.
- Find the app root and path alias (`@` -> `src/`) and the lint command before writing code; run lint on what you touch.
- If the repo has a code graph or index, query it for "where is X / what uses Y" before grepping. The most-connected
  modules (base entity, base list, URL/query helpers, shared enums) are where conventions are set.

## Where things go

| What | Path |
|---|---|
| Self-contained product feature (UI + logic) | `features/<feature-kebab>/{components,composables,config,types}/` |
| Entity-specific code | `entities/<entity>/{composables,store,views,providers}/` + `Entity.js`, `EntitiesList.js`, `config.js`, `fields.js` |
| Reusable UI primitives (`SButton`, `SModal`, `SCard`, `SConfirm`...) | `common/components/` |
| Reusable infra composables/stores (storage, consent, confirm) | `common/composables/`, `common/store/` |
| App-wide composables (`useStore`, `useRoute`, `useDict`, `useEventTracker`, `useInfiniteLoad`...) | `composables/` |
| Renderless provider components | `providers/` |
| Shared Pinia stores | `store/pinia/` |
| HTTP | `api/repositories/<domain>Repository.js`; helpers in `api/helpers/` |
| Dashboard widgets | `views/dashboard/widgets/<widget-kebab>/` (Widget, Row, Empty, Skeleton split) |
| Tracking constants / enums / breakpoints | `config/tracker.js`, `config/enums.js`, `config/breakpoints.js` |
| Route guards | `router/middleware/<camelCase>.js`, listed in route `meta.middleware` |

File naming: components `PascalCase.vue`; older shared primitives may be `kebab-case/kebab-case.vue` -
keep whichever the folder already uses. Composables and stores `useThing.js`. Entity classes `PascalCase.js`.

## Components

1. **New components use `<script setup>`**. File order: `<template>`, `<script setup>`, `<style lang="scss" scoped>`.
2. Script order: imports (vue -> libs -> `@/api` -> `@/composables` -> `@/config` -> `@/common`/`@/components` -> relative `./`) ->
   `defineProps` -> `defineEmits` -> `SCREAMING_SNAKE` constants -> composable calls -> template refs -> state refs -> computed -> functions -> lifecycle hooks last.
3. `defineProps` with the object syntax: `type` plus `default` (factory for objects/arrays) or `required: true`. Read props as `props.x`.
4. `defineEmits(['kebab-event'])`; emit one payload object: `emit('action-clicked', { id, payload, actionType })`.
5. Template refs are `const widget = ref();` and accessed as `widget.value?.$el` / `widget.value?.setup(...)`.
6. **Never mix `<script setup>` and an Options `<script>` in one file.** To use a composable in an Options-API file, add
   a `setup()` that returns what's needed (`const { trackViewDetail } = useThingTracker(); return { trackViewDetail };`) and call it as `this.trackViewDetail(...)`.
   A *small* Options component you are rewriting anyway may be converted whole to `<script setup>`; big legacy pages are extended in place.
7. Feature gating (e.g. `store.getters['Account/hasComponent']('feature_key')`) sits at the highest level that owns the surface:
   route meta + a validation guard for a page, `hasAccess: () => hasComponent('x')` in the list config for list
   types/bulk actions, `v-if="flagsAvailability.thing"` in the parent for a widget. When the gated value is also
   needed (a route, a link), one computed returns it or `undefined`, and the template tests only that.
8. Props going into a composable are passed as refs: `useThing(toRef(props, 'dataset'))`, never `props.dataset` (loses reactivity).
9. Never pass object/array literals as props in templates (`:options="{ a: 1 }"` can re-render forever) - hoist to a constant or computed. Function-typed props need a factory default: `default: () => () => false`.
10. Options-API hosts (e.g. `App.vue`) that must `provide` a composable get a renderless provider component in `providers/` (patterns.md §11).

### Template rules (enforced by eslint)

- One attribute per line once there's more than one; every element without content self-closes (`<div ref="sentinel" />`).
- Component tags in kebab-case (`<thing-row />`), registered by import in `<script setup>`.
- `:key` is a prefixed template string: `` :key="`thing-${item.id}`" ``.
- Text comes from the dictionary: `{{ $vDict('dashboard.thing_widget_title.text') }}`. Never hard-code UI strings (see **Translations**).
- Add `data-cy="..."` (or the project's test-id attribute) to actionable elements.
- Three states for any loaded block: skeleton (`*Skeleton.vue`) while `isInitialLoading`, content, and an `*Empty.vue` empty state. Use a spinner for "loading more".

### Styles

- `<style lang="scss" scoped>`, BEM with SCSS nesting: `.thing-widget { &__container {} &__loader {} &--loading {} }`. Root class = component name in kebab-case.
- Use design tokens: `var(--spacing-N)`, font and colour CSS vars, breakpoint mixins such as `@include media-breakpoint-up(sm)`. No hard-coded colours (see **Theming**).
- Prefer `display: grid` / `flex` + `gap`; logical props (`margin-block`, `margin-inline`) are fine.

## Composables

- Named export `export function useThing(...)`; a default export is fine for one-off entity trackers / feature composables. Match the folder.
- Options as one destructured object with defaults, documented with JSDoc `@param`/`@template`.
- Return a flat object of refs/computeds/functions. Expose state consumers must not write as `readonly(ref)`.
- Vue-instance bridges (`useStore`, `useRoute`, `useRouter`, `useDict`, `useNotifications`, `useEventTracker`)
  use `getCurrentInstance()` and **throw outside setup** - call them at the top of setup / the store factory, never inside callbacks, watchers or event handlers.
- Anything you create (IntersectionObserver, ResizeObserver, timeouts, window/media-query listeners) is torn down in `onBeforeUnmount` / `onUnmounted`.
  Whatever a child registers with a provider (getter funcs, focusable elements) it unregisters on unmount.
- Large composables are grouped with `// #region Name` ... `// #endregion`.
- Escape hatches are labelled: `// WARNING: use this function VERY sparingly` (forced remounts, registered getters).
- Responsive branches: `const { sizeMatches: isMobile } = useBreakpoint({ max: BREAKPOINT_LG })` over `matchMedia`. Render a different component on mobile (a modal instead of a side form) rather than hiding with CSS, and skip desktop-only observers.
- Forms: `useFormValidation(rules, formData)` (Vuelidate) -> `{ fieldState, formState, validateForm({ toastErrors, scrollToError }), resetValidation }`, wired to the form-group component's invalid/error props.
  Conditional rules via `requiredIf(() => ...)`. Dirty check against a snapshot taken on open: `initialFormData = cloneDeep(formData)`. Reset modal state on `@hidden`.
- Before writing a bridge, check what already exists (route/router, notifications, toaster, breakpoint, forced remount, form validation, generic entity form, hidden-element measurement).

### Provider / consumer (shared state for one subtree)

When a panel/page and its deep children share state, write a pair in one file:
`export const X_INJECTION_KEY = Symbol('x')`, `useXProvider(config)` that builds the context,
`provide`s it and returns it, and `useXConsumer()` that `inject`s it with a safe default.
Document the context shape with a JSDoc `@typedef` and annotate the consumer `@returns` (patterns.md §3).
Use a Pinia store instead only when the state outlives the subtree.

## State (Pinia)

- **Setup stores only**: `defineStore('kebab-id', () => { ...; return { ... } })`, exported as `useXStore`.
- Non-serialisable or transient values (a pending promise `resolveFn`, timers) live in module-level `let`, not in state (patterns.md §4).
- In components destructure with `storeToRefs(store)`; call actions on the store object.
- Replace, don't mutate: `entities.value = { ...entities.value, [id]: entity }`, `const { [id]: _, ...rest } = obj`, `items.value = [...items.value, ...more]` - Vue 2 can't see added keys, and replacement keeps Vue 3 watchers simple too.
- Per-key stores use a registry factory (`useNormalizedEntityStore(Entity.entityKey)` -> `upsertOne/upsertMany/getById/getMany/remove/clear`).
  Fill it where the data is fetched (`storeEntity(entity)` after load), not from a watcher; clear shared stores in `router.beforeEach` when `to.path !== from.path`, not in `afterEach`.
- Browser storage goes through `useLocalStorage()` / `useSessionStorage()` wrappers (JSON-safe), never `localStorage` directly; namespace keys with a `STORAGE_KEY` constant and a `buildKey(...)` helper.

## API calls

- Repository functions: a `BACKEND_ENDPOINTS` map of `{ url }`, path params via a `replacePath(url, { thing_id })` helper,
  query via a `toGetParams(endpoint, data)` helper, transport via `BaseRepository.get/post/patch/put/delete`.
  Signature `(data = null, config = {})`, spread `...config` last.
- List query shape (adapt to the backend): `{ filter: { field: { eq | in | not_in | gte | lte } }, sort: { field: { order } }, limit, offset, language_id }`, responses `{ data, meta: { total } }`.
- Call pattern inside components/composables:

  ```js
  isLoading.value = true;
  try {
    const resp = await getThings(query);
    if (!resp?.data) throw new Error('Invalid data');
    items.value = resp.data;
  } catch (error) {
    console.error(error);
    $notifUnexpectedError();
  } finally {
    isLoading.value = false;
  }
  ```

  Per-row actions keep a loading map: `actionLoading.value = { ...actionLoading.value, [id]: actionType }`, cleared in `finally`.
- Requests that can overlap (filters, charts, prev/next navigation) are latest-wins via a cancellation helper (patterns.md §12): `catch (e) { if (e.isCancelled) return; ... }`.
- One-shot async calls several mounts may trigger (identify, token fetch) get a module-level re-entry flag reset in `finally`; per-id batch loaders keep a module-level in-flight list and skip ids already in the store.
- Pass the body straight through: `BaseRepository.post(url, data)` - not `{ data }`. PATCH payloads pick allowed fields explicitly; don't send the whole form with unwanted keys set to `undefined`.
- "Not loaded yet" is `null`, distinct from `0`/`[]`: `x ? x.count : null`, never `|| 0`. Async loader status for a subtree: `provide('isAsyncLoading', getLoadingStatus)` over a `reactive({ ... })` map.
- After a create/edit, update the list locally instead of refetching (`list.items = [entity, ...list.items]; list.total += 1;`, `entity.patchData(payload)` for edits). Rebuild derived arrays from the source, never overwrite them with one row's data.
- Paged lists / "show more" / infinite scroll: `useInfiniteLoad({ fetchFn, pageSize, enabled, sentinelEl, rootEl, hasMoreFn })` with a `<div ref="infiniteSentinel" />` at the end of the list.
- Wrap API rows in entity classes when the UI needs entity behaviour (`new Thing(data)`, a factory by type id).

## Mocking the API (backend not ready)

- **Quick UI-first pass**: render against fake data plus clearly marked placeholder strings, then a
  `feat: backend integration` commit adds the real repository function and a follow-up deletes the fake.
  Prefer a separate `api/mock-repositories/` mirror over a fake inside the real repository function; never merge a fake without the follow-up.
- **Realistic paging, latency, errors**: a local mock server (e.g. json-server) with fixtures in the backend envelope,
  routes that honour filters/limit and a configurable delay, and a front-end mirror repository with the same exports.
  Only the import changes when the backend lands (patterns.md §10). Point only the mirrored calls at the mock - not the global API base URL.

## Translations (vocabulary)

UI text lives in a backend dictionary maintained in a translation-management tool, cached on the client and read through a
dictionary helper (`$vDict` in templates, `useDict()` in script).

- **Key format:** `<section>.<snake_key>.text`, where `<section>` is a vocabulary section the screen loads. Reuse an existing key before adding one.
- **Adding a text:** add the key and every supported language in the translation tool, then run the project's sync so the backend
  serves it. Until then use a `[NL]`-prefixed literal (`'[NL] Analysis'`) so it's obvious in review, and replace every `[NL]` before merge.
- **New section:** list it in the route's dictionary meta; sections from all matched routes are merged. If a section isn't loaded the helper silently returns `''`.
- Parameters use `{name}` placeholders: `$vDict('search.amount.text', { amount })`. Plurals use a sibling `<key>_plural` selected by a count param.
- Missing keys fall back to the fallback language, then `''`. Never fake text with `||` defaults.
- Lookups (select options, enum names) are a separate dictionary type with their own helper and sync.
- Dictionary text containing HTML is rendered with `v-html`, only for trusted dictionary content.

## Event tracking

- Never call the analytics SDK directly. Use `const { track } = useEventTracker();` and
  `track(COMMON_EVENTS.CLICK.key, EVENT_SUBJECT.widget, { source, target })` - the tracker builds the event name and adds default params (user, client, route, utm).
- New surfaces add constants to `config/tracker.js`: an `EVENT_SOURCES` entry, `EVENT_SUBJECT` if new, and an `EVENT_STATIC.<surface> = { source, actionables: { snake_case: 'snake_case' } }` block.
- Entity events go through an entity tracker composable (`entities/<e>/composables/use<E>Tracker.js`) that spreads the common entity trackers and adds its own `trackX`; ids are stringified.
- "Viewed" events fire once on visibility (IntersectionObserver, disconnect after the first hit).
- Journeys/sources are super-properties set by helpers and reset by a route middleware.

## Router

- Middleware: `export default async function name({ from, to }) { ... }` in `router/middleware/`, listed in `meta.middleware`.
  Guard same-route query changes with `if (from.name === to.name) return;` and a `// Note:` explaining why.
- Lazy routes are `component: () => import(...)` - a bare `import(...)` is a bug.
- Shared route setup (dictionary sections, layout) goes through container helpers that `merge` a shared config; don't copy-paste route meta between pages - a copied licence key gates the page wrongly.

## Legacy entity / list classes

Most real features still extend a base entity, a base list, list `config.js` and Options-API mixins. Extend them the way the classes expect:

- **Template-method hooks, not branches or patches.** Override a method or static on a subclass
  (`getListTypeId()`, `getExcludeFilterStatuses()`, `static statusesShownAsEmpty = [...]`), and give it its own list-type entry if it's a new list.
  Never `Object.assign(listInstance, { beforeLoadingItems })`.
- New behaviour switches are list-config flags with a default in the base list's initial config and a why-comment (`skipsPageParams: false, // for non main list ...`). Two lists on one page must not share the page query param.
- Business rules live on the entity (`canChangeStatus()`); call sites only ask `!entity.canChangeStatus()`. If the same check appears in three components, move it into the entity.
- If subclasses set a `this.selfClass = SubClass` reference after `super()`, the base **constructor** still sees the base class - anything a constructor needs from statics is read via `this.constructor.x`, and statics are never read off the instance.
- A class put in a normalised store needs `static entityKey`; add it to the class instead of patching callers. Use the collection's own membership API (`.hasById(id)`), not `hasOwnProperty`.
- Search-field transforms are config hooks (`transformInput(value, options)`, a key -> transform-type map), not inline code in the component.
  A new search filter usually touches the endpoint filter-type map, the list config default filter, and the search component's filter definition - change all three.

## Theming (light / dark / brand)

- Themes are runtime stylesheets of semantic tokens over a palette (`--color-text-mute: var(--color-grey-700)`). Components use only semantic tokens.
- Translucency uses RGB helpers: `rgb(var(--color-surface-primary-rgb) / var(--alpha-10))`. Each `*-rgb` helper carries a comment with the hex it mirrors and is re-derived when that hex changes.
- Never hard-code `#fff`, `white`, `#000` or `rgba(...)` - it causes "white on white" in dark mode. Exception: print is pinned light.
- Colours stored in data (tags, labels) are mapped to `var(--color-tag-*)` tokens, not rendered raw.
- JS that needs colours (charts) reads CSS vars through a composable that re-reads on theme change (a theme-instance key bumped when the stylesheet loads); consumers remount via a forced-remount key. Never read CSS vars once at module load.
- Brand theme and luminance (auto/light/dark) are separate choices; `auto` follows `prefers-color-scheme`. Check every UI change in dark mode, on mobile and in print.

## Environment & third-party widgets

- Per-environment values are build-time env vars in every `.env*` file, exposed to `index.html` through one `window.APP_CONFIG = { ... }` object. Don't gate features by hostname maps in `index.html`.
- Locale-specific links switch on the locale/domain inside a computed.
- Third-party widgets (chat, analytics): pre-seed their settings before the script loads to avoid races (e.g. "don't load immediately"), queue calls on their ready hook, hide them on print routes, and keep the wrapper in one composable. Never commit real tokens/JWTs, even as placeholders.

## Delivering a feature

One small commit per step:

1. **Clear the ground** - remove the thing being replaced first, or refactor the code you're about to touch in its own commit.
2. **Scaffold** - entity/list/config/route or the feature folder, with `[NL]` texts and the API mocked (see **Mocking**).
3. **UI** - components and states (skeleton/empty/error), wired to existing state first; extract a component once its shape settles.
4. **Integrate** - real repository functions. Expect the contract to move (URL, method, key names such as `type` -> `type_id`); keep endpoint and key names in one place so a rename is a one-line fix, and re-check the real response shape before closing.
5. **Gate and finish the text** - licence/feature checks, dictionary keys replacing `[NL]`, tracking constants.
6. **Polish** - `fix:` commits for styling, mobile, dark mode, accessibility; `refactor: apply review` after PR review.

A quick "works but ugly" version is acceptable on a branch only if it's labelled and the next commit replaces it with the proper hook.

## Fixing a bug

- **Find the root cause and fix it at the source**: the entity method, the repository, the store lifecycle, the missing static. Patch the call site only when the issue is truly local UI.
- Keep the fix small (usually 1-3 files, a few lines) and in its own commit. Don't let the formatter rewrite untouched parts of a legacy file in the same commit; reformat separately, or not at all.
- Check these categories first - they recur:
  - **Race / re-entry**: double init, overlapping requests, reading `$refs` before `$nextTick`, a third-party script not ready -> latest-wins cancellation, a re-entry flag with `try/finally`, `$nextTick`, ready queues.
  - **Unawaited async**: an action that returns nothing; return the promise and `await` it.
  - **Shared mutation**: an object reused as a query filter -> clone before passing it; replace reactive collections instead of mutating them.
  - **Contract drift**: renamed backend key, emitted payload shape changed without its consumer, map indexed by the wrong key, string vs number ids, sign-encoded ids (`Math.abs`).
  - **Missing guards on legacy data**: `?.` on optional nested fields.
  - **Styling**: hard-coded colours (dark mode), contrast, very wide and mobile layouts, floating widgets overlapping content.
- If a release breaks, revert in its own commit, then reapply the corrected change - don't stack hotfixes on a broken merge.
- Add test ids on new actionable elements. When a fix is in pure logic (entity methods, helpers, composables), add a unit test if the project has a test setup.

## Known traps

- `console.log` / junk literals committed - remove them before committing, not in a later "remove log" commit.
- IDE auto-imports (`import { _ } from 'core-js'`, `import _, { update } from 'lodash'`) - delete on sight.
- Route meta copied from another page.
- Testing a computed ref without `.value` in script (`if (hasAccess)` is always truthy).
- Composable calls inside `watch` callbacks or event handlers.
- Unbounded stacks/caches - cap them.
- Magic ids in filters (`type_id: { not_in: [15] }`) - add an enum constant.
- Building a new entity on edit instead of patching the existing one.
- Names that lie: rename when the meaning changes; flag temporary bridges with `// NOTE:` and why they must go.

## Code style

- Prettier: single quotes, semicolons, trailing commas, 2 spaces; airbnb-base. `?` and `:` break **before**, other operators after.
- Early returns over nesting; blank line before `return` and between logical blocks.
- Optional chaining / nullish (`?.`, `??`) are used freely.
- Constants at module top in `SCREAMING_SNAKE`; enum lookups via `ENUM.properties[id]?.field`.
- Comments are rare, lower-case, and explain *why* (`// Note: ...`). No banner comments on new files.
- No-op overrides in subclasses silence lint inline: `// eslint-disable-next-line class-methods-use-this`.
- Commits use lowercase conventional prefixes that match the change: `feat:`, `fix:`, `hotfix:`, `refactor:`, `chore:` (log removal, moves). A removed log or typo is not `feat:`. Follow the repo's ticket-key convention for PR titles.
- Legacy exceptions you'll see and shouldn't copy: direct `localStorage`, in-place `delete obj[key]` on reactive state, hard-coded English on internal admin pages, hex colours in older components.

## Checklist before finishing

- [ ] `<script setup>` for new components; Options-API files only got a `setup()` bridge.
- [ ] All text via the dictionary, keys exist and are synced, new sections listed in the route meta; licensed features gated.
- [ ] Loading, empty and error paths handled; `finally` resets flags; errors -> `console.error` + the user-facing error notification.
- [ ] Reactive updates are replacements, not in-place adds.
- [ ] Observers/timers/listeners cleaned up; child registrations unregistered.
- [ ] Tracking constants added; calls go through the tracker composable.
- [ ] No leftover fake data, `[NL]` placeholders or mock-repository imports in code meant for merge.
- [ ] Bug fixed at its source; the diff contains only the fix; overlapping requests are cancelled or de-duplicated.
- [ ] No hard-coded colours; checked in dark mode, on mobile and in print.
- [ ] No `console.log`, stray auto-imports, real tokens or copy-pasted route meta.
- [ ] One attr per line, self-closing tags, scoped BEM SCSS with tokens.
- [ ] Lint passes for the touched files.
