# Templates

Neutral shapes (`Thing`, `useThing`...). Copy the shape, rename to your domain, swap helper names for the project's equivalents, delete what you don't need.

## 1. Widget / feature component (`<script setup>`)

```vue
<template>
  <dashboard-widget
    ref="widget"
    :title="$vDict('dashboard.thing_widget_title.text')"
    class="thing-widget"
    @viewed="trackView"
  >
    <thing-skeleton v-if="isInitialLoading" />

    <div
      v-else-if="items.length"
      ref="listContainer"
      class="thing-widget__container"
    >
      <thing-row
        v-for="item in items"
        :key="`thing-${item.id}`"
        :item="item"
        :loading="actionLoading[item.id]"
        @action-clicked="doAction"
      />

      <v-spinner
        v-if="isLoadingMore"
        class="thing-widget__loader"
      />
      <div ref="infiniteSentinel" />
    </div>

    <thing-empty
      v-else
      :text="$vDict('dashboard.thing_empty.text')"
    />
  </dashboard-widget>
</template>

<script setup>
import { onMounted, ref } from 'vue';
import { getThings, patchThing } from '@/api/repositories/thingRepository';
import { useDict } from '@/composables/useDict';
import { useStore } from '@/composables/useStore';
import { useNotifications } from '@/composables/useNotifications';
import { useInfiniteLoad } from '@/composables/useInfiniteLoad';
import { useEventTracker } from '@/composables/useEventTracker';
import { COMMON_EVENTS, EVENT_STATIC, EVENT_SUBJECT } from '@/config/tracker';
import VSpinner from '@/components/VSpinner.vue';
import DashboardWidget from '../../DashboardWidget.vue';
import ThingRow from './ThingRow.vue';
import ThingEmpty from './ThingEmpty.vue';
import ThingSkeleton from './ThingSkeleton.vue';

const props = defineProps({
  widgetId: {
    type: Number,
    required: true,
  },
  settings: {
    type: Object,
    default: () => ({}),
  },
});

const PAGE_SIZE = 5;

const { $getDictLanguage } = useDict();
const store = useStore();
const { $notifUnexpectedError } = useNotifications();
const { track } = useEventTracker();

const widget = ref();
const listContainer = ref();
const infiniteSentinel = ref();

const actionLoading = ref({});

async function fetchThings({ offset, pageSize }) {
  const resp = await getThings({
    filter: {
      assigned_to: { in: [store.getters['Account/userId']] },
    },
    sort: { created_at: { order: 'a' } },
    offset,
    limit: pageSize,
    language_id: $getDictLanguage('id'),
  });

  if (!resp?.data) {
    throw new Error('Invalid data');
  }

  return resp;
}

const { init, items, isInitialLoading, isLoadingMore } = useInfiniteLoad({
  fetchFn: fetchThings,
  pageSize: PAGE_SIZE,
  sentinelEl: infiniteSentinel,
  rootEl: listContainer,
  hasMoreFn: (loaded, response) => response.meta.total > loaded.length,
});

async function doAction({ id, payload, actionType }) {
  track(COMMON_EVENTS.CLICK.key, EVENT_SUBJECT.widget, {
    source: EVENT_STATIC.dashboard_widget_thing.source,
    target: EVENT_STATIC.dashboard_widget_thing.actionables.mark_done,
  });

  try {
    actionLoading.value = { ...actionLoading.value, [id]: actionType };
    await patchThing(id, payload);
    items.value = items.value.filter((item) => item.id !== id);
  } catch (error) {
    console.error(error);
    $notifUnexpectedError();
  } finally {
    delete actionLoading.value[id];
    actionLoading.value = { ...actionLoading.value };
  }
}

function trackView() {
  track(COMMON_EVENTS.VIEW.key, EVENT_SUBJECT.widget, {
    target: EVENT_STATIC.dashboard_widget_thing.source,
  });
}

onMounted(async () => {
  await init();

  widget.value?.setup({ id: props.widgetId });
});
</script>

<style lang="scss" scoped>
.thing-widget {
  display: grid;
  grid-template-rows: auto 1fr;

  &__container {
    overflow-y: auto;
  }

  &__loader {
    margin-block: var(--spacing-8);
    margin-inline: auto;
  }
}
</style>
```

Child row emits a single payload object:

```js
const emit = defineEmits(['action-clicked']);

function markDone() {
  emit('action-clicked', { id: props.item.id, payload: { done: 1 }, actionType: 'mark' });
}
```

## 2. Bridging into an Options-API page

```js
export default {
  // ...existing options
  setup() {
    const { upsertOne: storeEntity } = useNormalizedEntityStore(Thing.entityKey);
    const { trackViewDetail } = useThingTracker();

    return {
      storeEntity,
      trackViewDetail,
    };
  },
  computed: {
    flagsAvailability() {
      return {
        thingInsights: this.$store.getters['Account/hasComponent']('thing_insights'),
      };
    },
  },
};
```

## 3. Provider / consumer composable

```js
import { inject, provide, ref, readonly, computed } from 'vue';

/**
 * @typedef {{
 * isLoading: Readonly<import('vue').Ref<boolean>>;
 * items: import('vue').ComputedRef<any[]>;
 * fetchItems: () => Promise<void>;
 * }} ThingPanelContext
 */

export const THING_PANEL_INJECTION_KEY = Symbol('thing-panel');

export function useThingPanelProvider(config = { sourceEntity: undefined }) {
  // #region Data & Fetch
  const isLoading = ref(false);
  const rawItems = ref([]);

  const items = computed(() => rawItems.value.filter(Boolean));

  async function fetchItems() {
    isLoading.value = true;

    try {
      // ...
    } catch (error) {
      console.error(error);
    } finally {
      isLoading.value = false;
    }
  }
  // #endregion

  const context = {
    isLoading: readonly(isLoading),
    items,
    sourceEntity: config.sourceEntity,
    fetchItems,
  };

  provide(THING_PANEL_INJECTION_KEY, context);

  return context;
}

/**
 * @returns {ThingPanelContext}
 */
export function useThingPanelConsumer() {
  return inject(THING_PANEL_INJECTION_KEY, {});
}
```

## 4. Pinia setup store with a promise-based action

A confirm dialog store; the dialog component is mounted once in the app root.

```js
import { defineStore } from 'pinia';
import { ref } from 'vue';

let resolveFn = null;

export const useConfirmStore = defineStore('confirm', () => {
  const open = ref(false);
  const title = ref('');

  function request(opts = { title: '' }) {
    title.value = opts.title;
    open.value = true;

    return new Promise((resolve) => {
      resolveFn = resolve;
    });
  }

  function accept() {
    open.value = false;
    resolveFn?.(true);
    resolveFn = null;
  }

  function cancel() {
    open.value = false;
    resolveFn?.(false);
    resolveFn = null;
  }

  return { open, title, request, accept, cancel };
});
```

Usage:

```js
const confirmStore = useConfirmStore();

if (!(await confirmStore.request({ title: $vDict('general.confirm_editor_title.text') }))) return;
```

In a component: `const { open, title } = storeToRefs(confirmStore);`

## 5. Repository function

```js
import { BaseRepository } from '@/api/baseRepository';
import { replacePath } from '@/shared/utils';
import { toGetParams } from '@/shared/api';

const BACKEND_ENDPOINTS = {
  THINGS: {
    url: '/v1/things',
  },
  THING: {
    url: '/v1/things/{thing_id}',
  },
};

export async function getThings(data = null, config = {}) {
  const endpoint = BACKEND_ENDPOINTS.THINGS;
  const params = toGetParams(endpoint, data);

  return BaseRepository.get(endpoint.url, { params, ...config });
}

export function patchThing(thing_id, data = {}) {
  const path = replacePath(BACKEND_ENDPOINTS.THING.url, { thing_id });

  return BaseRepository.patch(path, data);
}
```

## 6. Tracking

`config/tracker.js` - add the surface:

```js
export const EVENT_SOURCES = {
  // ...
  dashboard_widget_thing: 'dashboard_widget_thing',
};

export const EVENT_STATIC = {
  // ...
  dashboard_widget_thing: {
    source: EVENT_SOURCES.dashboard_widget_thing,
    actionables: {
      see_more: 'see_more',
      mark_done: 'mark_done',
    },
  },
};
```

Entity tracker - `entities/<entity>/composables/use<Entity>Tracker.js`:

```js
import { useCommonEventTracker, useEventTracker } from '@/composables/useEventTracker';
import { Thing } from '../Thing';

const EVENT_CATEGORY = Thing.entityKey;

const EVENTS = {
  CHANGE_STATUS: {
    key: 'status_change',
    label: 'status_updated',
  },
};

export default function useThingTracker() {
  const { track } = useEventTracker();
  const commonTrackers = useCommonEventTracker(EVENT_CATEGORY);

  function trackChangeStatus(thingData, oldStatus, newStatus, sourcePath) {
    track(EVENTS.CHANGE_STATUS.key, EVENT_CATEGORY, {
      event_label: EVENTS.CHANGE_STATUS.label,
      thing_id: `${thingData.id}`,
      old_status: oldStatus,
      new_status: newStatus,
      source_path: sourcePath,
    });
  }

  return {
    ...commonTrackers,
    trackChangeStatus,
  };
}
```

Seen-once tracking:

```js
const seenObserver = ref();

function onSeen(_observer, _ratio, entry) {
  if (entry.isIntersecting) {
    trackSeen(props.entity.data);
    seenObserver.value.disconnect();
  }
}

await nextTick();
if (sectionEl.value?.$el) {
  seenObserver.value = visibilityObserver(sectionEl.value.$el, onSeen, { threshold: 0.6 });
}

onUnmounted(() => {
  seenObserver.value?.disconnect();
});
```

## 7. Session-scoped feature storage

```js
import { useSessionStorage } from '@/common/composables/useSessionStorage';

const STORAGE_KEY = 'thing-feedback';

function buildKey(parentId, thingId) {
  return `${parentId}_${thingId}`;
}

export function useThingFeedback() {
  const session = useSessionStorage();

  function getAll() {
    return session.getItem(STORAGE_KEY, {});
  }

  function setFeedback(parentId, thingId, value) {
    const data = getAll();
    data[buildKey(parentId, thingId)] = value;
    session.setItem(STORAGE_KEY, data);
  }

  function getFeedback(parentId, thingId) {
    return getAll()[buildKey(parentId, thingId)] ?? null;
  }

  return { setFeedback, getFeedback };
}
```

## 8. Entity list subclass

```js
export class RelatedThingsList extends BaseThingsList {
  static entityClass = Thing;

  constructor(listTypeId) {
    super(listTypeId);

    // the base constructor ran before this line: statics it needs are read via this.constructor
    this.selfClass = RelatedThingsList;
  }

  // eslint-disable-next-line class-methods-use-this
  onSuccessLoadingItems() {
    // no op
  }

  asyncLoaders() {
    const parentLoaders = super.asyncLoaders();
    const statusesLoader = useEntityStatusesLoader(this.items, this.constructor.entityClass);

    return [...parentLoaders, statusesLoader];
  }
}
```

## 9. Router middleware

```js
export default async function thingMiddleware({ from, to }) {
  // Note: do not run on query-param change on the same page
  if (from.name === to.name) return;

  // ...
}
```

Registered in the route: `meta: { middleware: [thingMiddleware, validateFeature] }`.

## 10. Mocked endpoint (json-server)

Layout: a `mock/` package next to the app with `server.js`, `utils.js` (`returnResponse`, `filterItems`) and `endpoints/<area>/`.

Fixture - `mock/endpoints/things/things.json` (backend envelope; `MockRepository` returns `result`, throws `error`):

```json
{
  "result": {
    "meta": { "size": 2, "offset": 0, "total": 2 },
    "data": [
      { "id": 1, "name": "First" },
      { "id": 2, "name": "Second" }
    ]
  }
}
```

Routes - `mock/endpoints/things/index.js`:

```js
const things = require('./things.json');
const utils = require('../../utils');

module.exports = function useThings(server) {
  server.get('/v1/things', (request, response) => {
    utils.returnResponse(response, { data: things });
  });

  server.post('/v1/things/query', (request, response) => {
    utils.returnResponse(response, {
      data: utils.filterItems(request.body.query, things),
    });
  });

  server.patch('/v1/things/:id', (request, response) => {
    utils.returnResponse(response, {
      code: 200,
      timeout: 500,
      data: { result: { id: request.params.id, ...request.body } },
    });
  });
};
```

Register in `mock/server.js` before `server.use(router)`:

```js
const useThings = require('./endpoints/things');
// ...
useThings(server);
```

Front-end mirror - `api/mock-repositories/thingRepository.js` (same exports as the real repository):

```js
import { MockRepository } from '@/api/mockRepository';
import { replacePath } from '@/shared/utils';
import { toGetParams } from '@/shared/api';

const BACKEND_ENDPOINTS = {
  THINGS: {
    url: '/v1/things',
  },
  THING: {
    url: '/v1/things/{thing_id}',
  },
};

export function getThings(data = null, config = {}) {
  const endpoint = BACKEND_ENDPOINTS.THINGS;
  const params = toGetParams(endpoint, data);

  return MockRepository.get(endpoint.url, { params, ...config });
}

export function patchThing(thing_id, data = {}) {
  const path = replacePath(BACKEND_ENDPOINTS.THING.url, { thing_id });

  return MockRepository.patch(path, data);
}
```

Consumer - only the import changes when the backend lands:

```js
import { getThings, patchThing } from '@/api/mock-repositories/thingRepository';
// -> import { getThings, patchThing } from '@/api/repositories/thingRepository';
```

Run:

```sh
cd mock && npm install && npm run serve   # mock server
npm run serve                             # app, with the mock base URL in .env.local
```

## 11. Renderless provider for an Options-API host

Wrapped around the app (or a subtree) by an Options-API host such as `App.vue`.

```vue
<script>
import { provide } from 'vue';
import {
  THING_INJECTION_KEY,
  useThingProvider,
} from '@/composables/useThing';

export default {
  name: 'ThingProvider',
  setup() {
    const thing = useThingProvider();

    provide(THING_INJECTION_KEY, thing);

    return {
      thing,
    };
  },
  render() {
    return this.$scopedSlots.default();
  },
};
</script>
```

## 12. Latest-wins request

```js
import { createSingleCancellableApiCall } from '@/api/helpers/cancel-helper';
import { getThingStats } from '@/api/repositories/thingRepository';

// module level: every call cancels the previous in-flight one
const fetchThingStats = createSingleCancellableApiCall(getThingStats);

async function loadStats(query) {
  isLoading.value = true;
  try {
    const response = await fetchThingStats(query);
    stats.value = response?.data || {};
  } catch (error) {
    if (error.isCancelled) return;

    console.error(error);
    $notifUnexpectedError();
  } finally {
    isLoading.value = false;
  }
}
```

The repository function must take `(data = null, config = {})` and spread `...config` into the `BaseRepository` call so the `cancelToken` reaches axios.
