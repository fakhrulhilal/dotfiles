# Talking to the backend

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

### `src/backend/http.ts`

```ts
// Fixed at build time from TODO_API_URL (vite.config.ts); empty means this site's own address.
const API_URL = __TODO_API_URL__.replace(/\/+$/, '');

const UNREACHABLE = 'The todo service is unreachable.';
const UNEXPECTED = 'The todo service sent an unexpected answer.';

export class ApiError extends Error { }

export function errorMessage(error: unknown): string {
    return error instanceof ApiError ? error.message : UNEXPECTED;
}

export async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
    let response: Response;
    try {
        response = await fetch(API_URL + path, {
            method,
            headers: body === undefined ? undefined : { 'content-type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
    } catch {
        throw new ApiError(UNREACHABLE);
    }
    if (!response.ok) throw new ApiError(await problemMessage(response));
    if (response.status === 204) return undefined as T;
    try {
        return (await response.json()) as T;
    } catch {
        throw new ApiError(UNEXPECTED);
    }
}

interface Problem {
    title?: string;
    detail?: string;
    errors?: Record<string, string[]>;
}

export async function problemMessage(response: Response): Promise<string> {
    // A body that is not a problem document still gets the generic message below.
    const problem: Problem = await response.json().catch(() => ({}));
    const invalid = Object.entries(problem.errors ?? {})
        .sort(([a], [b]) => a.localeCompare(b))
        .flatMap(([member, details]) => details.map((detail) => `${member} ${detail}`));
    if (invalid.length > 0) return invalid.join('; ');
    return problem.detail ?? problem.title ?? `Request failed (${response.status}).`;
}
```

## `src/backend/<resource>.ts`: the endpoints

Types are written by hand from the OpenAPI document (keep the JSON field names, e.g.
`created_at`), next to small pure helpers that mirror the server's rules (here: unknown filter
values mean `all`, which todos a filter shows).

### `src/backend/todos.ts`

```ts
import { send } from './http';

// The types follow contract/openapi.json; this client cannot use the Rust contract crate.

export interface Todo {
    id: string;
    title: string;
    completed: boolean;
    created_at: string;
    updated_at: string;
}

export interface UpdateTodo {
    title?: string;
    completed?: boolean;
}

export type StatusFilter = 'all' | 'active' | 'completed';

export const STATUS_FILTERS: readonly StatusFilter[] = ['all', 'active', 'completed'];

export const MAX_TITLE_CHARS = 200;

// Unknown values fall back to `all`, as in the contract crate.
export function parseFilter(value: unknown): StatusFilter {
    return STATUS_FILTERS.find((filter) => filter === value) ?? 'all';
}

export function matchesFilter(filter: StatusFilter, todo: Todo): boolean {
    return filter === 'all' || (filter === 'completed') === todo.completed;
}

export function listTodos(filter: StatusFilter): Promise<Todo[]> {
    return send<Todo[]>('GET', `/api/v1/todos?status=${filter}`);
}

export function createTodo(title: string): Promise<Todo> {
    return send<Todo>('POST', '/api/v1/todos', { title });
}

export function updateTodo(id: string, changes: UpdateTodo): Promise<Todo> {
    return send<Todo>('PATCH', `/api/v1/todos/${id}`, changes);
}

export async function deleteTodo(id: string): Promise<void> {
    await send<void>('DELETE', `/api/v1/todos/${id}`);
}
```

## `src/stores/<resource>.ts`: the state

`createStore(fn, seed, { key })` is a **writable** store whose source is async: the function
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

### `src/stores/todos.ts`

```ts
import { createSignal, createStore, refresh } from 'solid-js';
import { listTodos, matchesFilter, type StatusFilter, type Todo } from '../backend/todos';
import { errorMessage } from '../backend/http';

export type Todos = ReturnType<typeof createTodos>;

export function createTodos(filter: () => StatusFilter) {
    // Writable, so each todo the API saves is applied without loading the list again.
    const [items, setItems] = createStore((): Promise<Todo[]> => listTodos(filter()), [] as Todo[], {
        key: 'id',
    });
    const [changeError, setChangeError] = createSignal('');

    // Used inside an action as `yield* todos.attempt(call)`; undefined when the call failed.
    function* attempt<T>(call: Promise<T>): Generator<Promise<T>, T | undefined, T> {
        setChangeError('');
        try {
            return yield call;
        } catch (failure) {
            setChangeError(errorMessage(failure));
            return undefined;
        }
    }

    function applySaved(todo: Todo) {
        const shown = matchesFilter(filter(), todo);
        setItems((list) => {
            const index = indexOfTodo(list, todo.id);
            // Field by field: replacing the object would re-run every reader of the row.
            if (index >= 0 && shown) Object.assign(list[index], todo);
            else if (index >= 0) list.splice(index, 1);
            else if (shown) list.push(todo);
        });
    }

    function remove(id: string) {
        setItems((list) => {
            const index = indexOfTodo(list, id);
            if (index >= 0) list.splice(index, 1);
        });
    }

    return {
        items,
        changeError,
        clearChangeError: () => setChangeError(''),
        attempt,
        applySaved,
        remove,
        reload: () => refresh(items),
    };
}

function indexOfTodo(list: Todo[], id: string): number {
    return list.findIndex((item) => item.id === id);
}
```

### Why this primitive

| Option | Why not |
|---|---|
| `createMemo(() => listTodos(filter()))` | Read-only: showing a change needs `refresh()`, a GET after every change |
| `createProjection(...)` | Read-only for the same reason |
| `createOptimisticStore(...)` | Its writes revert when the action settles; the API's answer would vanish unless followed by a refetch |
| `createStore(fn, seed, { key })` | Chosen: the source loads, writes apply the API's answer, one request per change |

## Components that use it

The page component creates the store, reads the filter from the address, and shows the list inside
`<Errored>` and `<Loading>`. Retry is `todos.reload()` (`refresh(items)`) then the boundary's
`reset()`. "Adding" is an optimistic flag: set at the start of the action, it falls back to `false`
when the action settles, whatever happened. A failed change shows in an `Alert` above the list.

### `src/components/TodoList.tsx`

```tsx
import { Alert } from "@kobalte/core/alert";
import { Button } from "@kobalte/core/button";
import { Skeleton } from "@kobalte/core/skeleton";
import { TextField } from "@kobalte/core/text-field";
import { useSearchParams } from "@solidjs/router";
import {
    action,
    createMemo,
    createOptimistic,
    Errored,
    For,
    Loading,
    Match,
    Show,
    Switch,
} from "solid-js";
import {
    createTodo,
    MAX_TITLE_CHARS,
    parseFilter,
    STATUS_FILTERS,
    type StatusFilter,
} from "../backend/todos";
import { focusOnMount } from "../focus";
import { errorMessage } from "../backend/http";
import { createTodos } from "../stores/todos";
import * as ui from "../ui";
import TodoItem from "./TodoItem";

const FILTER_LABELS: Record<StatusFilter, string> = {
    all: "All",
    active: "Active",
    completed: "Completed",
};

export default function TodoList() {
    // The filter lives in the address (`/?filter=active`), so reloads, bookmarks and the
    // back button keep it on any static host.
    const [search] = useSearchParams();
    const filter = createMemo(() => parseFilter(search.filter));
    const todos = createTodos(filter);

    const [adding, setAdding] = createOptimistic(false);
    const add = action(function* (form: HTMLFormElement) {
        setAdding(true);
        const title = String(new FormData(form).get("title") ?? "");
        const todo = yield* todos.attempt(createTodo(title));
        if (!todo) return;
        form.reset();
        todos.applySaved(todo);
    });

    return (
        <section class={ui.card}>
            <form
                class="flex gap-2"
                onSubmit={(event) => {
                    event.preventDefault();
                    add(event.currentTarget);
                }}
            >
                <TextField class="flex-1" name="title" required>
                    <TextField.Input
                        ref={focusOnMount()}
                        class={ui.input}
                        placeholder="What needs doing?"
                        aria-label="New todo"
                        maxlength={MAX_TITLE_CHARS}
                    />
                </TextField>
                <Button
                    type="submit"
                    class={ui.button.primary}
                    disabled={adding()}
                >
                    Add
                </Button>
            </form>

            <Show when={todos.changeError()}>
                <Alert class={ui.alert}>{todos.changeError()}</Alert>
            </Show>

            <nav
                aria-label="Filter todos"
                class="inline-flex w-fit rounded-lg bg-muted p-1 text-sm font-medium text-muted-foreground"
            >
                <For each={STATUS_FILTERS}>
                    {(option) => (
                        // aria-current is set here: the router marks the link equal to the address,
                        // which is none for an unknown filter, while the page shows All.
                        <a
                            href={filterHref(option)}
                            aria-current={filter() === option
                                ? "page"
                                : "false"}
                            class="rounded-md px-3 py-1 transition-colors hover:text-foreground aria-[current=page]:bg-background aria-[current=page]:text-foreground aria-[current=page]:shadow-sm"
                        >
                            {FILTER_LABELS[option]}
                        </a>
                    )}
                </For>
            </nav>

            <Errored
                fallback={(failure, reset) => (
                    <Alert
                        class={[
                            ui.alert,
                            "flex items-center justify-between gap-4",
                        ]}
                    >
                        <span>{errorMessage(failure())}</span>
                        <Button
                            class={ui.button.outline}
                            onClick={() => {
                                todos.reload();
                                reset();
                            }}
                        >
                            Retry
                        </Button>
                    </Alert>
                )}
            >
                <Loading
                    fallback={
                        <div
                            class="flex flex-col gap-3"
                            aria-busy="true"
                            aria-label="Loading"
                        >
                            <Skeleton class={[ui.skeleton, "h-9 w-full"]} />
                            <Skeleton class={[ui.skeleton, "h-9 w-full"]} />
                            <Skeleton class={[ui.skeleton, "h-9 w-2/3"]} />
                        </div>
                    }
                >
                    <ul class="flex flex-col divide-y">
                        <For each={todos.items} keyed={(todo) => todo.id}>
                            {(todo) => <TodoItem todo={todo()} todos={todos} />}
                        </For>
                    </ul>
                    <Switch>
                        <Match when={todos.items.length === 0}>
                            <div class={ui.empty}>
                                <p class="text-sm text-muted-foreground">
                                    Nothing to show here.
                                </p>
                            </div>
                        </Match>
                        <Match when={todos.items.length === 1}>
                            <p class="text-sm text-muted-foreground">1 todo</p>
                        </Match>
                        <Match when={todos.items.length > 1}>
                            <p class="text-sm text-muted-foreground">
                                {todos.items.length} todos
                            </p>
                        </Match>
                    </Switch>
                </Loading>
            </Errored>
        </section>
    );
}

function filterHref(filter: StatusFilter): string {
    return filter === "all" ? "/" : `/?filter=${filter}`;
}
```

A row: each change is an action; `busy` is optimistic; the checkbox shows an optimistic
`completed`, so a refused change puts it back by itself. The rename box keeps plain `open`/`saving`
variables next to the signal because reads lag writes: removing the box blurs it, which calls `save`
again before `editing()` has changed.

### `src/components/TodoItem.tsx`

```tsx
import { Button } from "@kobalte/core/button";
import { Checkbox } from "@kobalte/core/checkbox";
import { TextField } from "@kobalte/core/text-field";
import { action, createOptimistic, createSignal, Show } from "solid-js";
import { deleteTodo, MAX_TITLE_CHARS, type Todo, updateTodo } from "../backend/todos";
import { selectOnMount } from "../focus";
import type { Todos } from "../stores/todos";
import * as ui from "../ui";

export default function TodoItem(props: { todo: Todo; todos: Todos }) {
    // Optimistic, so it falls back to false by itself when the action settles.
    const [busy, setBusy] = createOptimistic(false);
    // Optimistic, so a change the API refuses puts the box back by itself.
    const [completed, setCompleted] = createOptimistic(() =>
        props.todo.completed
    );

    const toggle = action(function* (checked: boolean) {
        setBusy(true);
        setCompleted(checked);
        const todo = yield* props.todos.attempt(
            updateTodo(props.todo.id, { completed: checked }),
        );
        if (todo) props.todos.applySaved(todo);
    });

    const remove = action(function* () {
        setBusy(true);
        const id = props.todo.id;
        const deleted = yield* props.todos.attempt(
            deleteTodo(id).then(() => true),
        );
        if (deleted) props.todos.remove(id);
    });

    // `open` and `saving` mirror the state at once, unlike a signal's read: removing
    // the box blurs it, which calls save again before the signal has changed.
    const [editing, setEditing] = createSignal(false);
    let open = false;
    let saving = false;
    const startEditing = () => {
        open = true;
        setEditing(true);
    };
    const close = () => {
        open = false;
        setEditing(false);
    };

    const rename = action(function* (title: string) {
        setBusy(true);
        saving = true;
        const todo = yield* props.todos.attempt(
            updateTodo(props.todo.id, { title }),
        );
        saving = false;
        if (!todo) return;
        close();
        props.todos.applySaved(todo);
    });

    const save = (title: string) => {
        if (!open || saving) return;
        if (title.trim() === props.todo.title) close();
        else rename(title);
    };

    const onKeyDown = (
        event: KeyboardEvent & { currentTarget: HTMLInputElement },
    ) => {
        if (event.key === "Enter") {
            event.preventDefault();
            save(event.currentTarget.value);
        } else if (event.key === "Escape") {
            props.todos.clearChangeError();
            close();
        }
    };

    return (
        <li class="flex items-center gap-3 py-2">
            <Checkbox
                class={ui.checkbox.root}
                checked={completed()}
                disabled={busy()}
                onChange={(checked) => toggle(checked)}
            >
                <Checkbox.Input aria-label="Done" style={ui.checkbox.inputStyle} />
                <Checkbox.Control class={ui.checkbox.control}>
                    <Checkbox.Indicator>
                        <svg
                            class={ui.checkbox.indicator}
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="3"
                            stroke-linecap="round"
                            stroke-linejoin="round"
                            aria-hidden="true"
                        >
                            <path d="M20 6 9 17l-5-5" />
                        </svg>
                    </Checkbox.Indicator>
                </Checkbox.Control>
            </Checkbox>
            <Show
                when={editing()}
                fallback={
                    <span
                        class={[
                            "min-w-0 flex-1 break-words",
                            {
                                "text-muted-foreground line-through":
                                    props.todo.completed,
                            },
                        ]}
                    >
                        {props.todo.title}
                    </span>
                }
            >
                <TextField class="flex-1" defaultValue={props.todo.title}>
                    <TextField.Input
                        ref={selectOnMount()}
                        class={[ui.input, "h-8"]}
                        aria-label="Title"
                        maxlength={MAX_TITLE_CHARS}
                        onKeyDown={onKeyDown}
                        onBlur={(
                            event: FocusEvent & { currentTarget: HTMLInputElement },
                        ) => save(event.currentTarget.value)}
                    />
                </TextField>
            </Show>
            <Button
                class={ui.button.ghost}
                disabled={busy()}
                onClick={startEditing}
            >
                Edit
            </Button>
            <Button
                class={ui.button.danger}
                disabled={busy()}
                onClick={() => remove()}
            >
                Delete
            </Button>
        </li>
    );
}
```

## Tests

Unit tests cover the plain TypeScript (problem messages, the network failure, filters); behaviour
in the browser is tested end to end with Playwright against a running backend.

### `src/backend/http.test.ts`

```ts
import { afterEach, describe, expect, test, vi } from 'vitest';
import { ApiError, problemMessage, send } from './http';

function problem(status: number, body: unknown): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/problem+json' },
    });
}

describe('problemMessage', () => {
    test('lists the invalid members of a validation problem', async () => {
        const response = problem(422, {
            title: 'Unprocessable Entity',
            detail: 'One or more members of the request are invalid.',
            errors: { title: ['must not be empty'], completed: ['must be a boolean'] },
        });
        expect(await problemMessage(response)).toBe('completed must be a boolean; title must not be empty');
    });

    test('prefers the detail, then the title', async () => {
        expect(await problemMessage(problem(404, { title: 'Not Found', detail: 'No todo has that id.' }))).toBe(
            'No todo has that id.',
        );
        expect(await problemMessage(problem(404, { title: 'Not Found' }))).toBe('Not Found');
    });

    test('falls back to the status when the body is not a problem', async () => {
        expect(await problemMessage(new Response('oops', { status: 502 }))).toBe('Request failed (502).');
    });
});

describe('send', () => {
    afterEach(() => vi.unstubAllGlobals());

    test('a network failure says the service is unreachable', async () => {
        vi.stubGlobal('fetch', () => Promise.reject(new TypeError('Failed to fetch')));
        await expect(send('GET', '/api/v1/todos')).rejects.toEqual(new ApiError('The todo service is unreachable.'));
    });
});
```

### `src/backend/todos.test.ts`

```ts
import { describe, expect, test } from 'vitest';
import { matchesFilter, parseFilter, type Todo } from './todos';

describe('filters', () => {
    const todo = (completed: boolean) => ({ completed }) as Todo;

    test('unknown values mean all', () => {
        expect(parseFilter('completed')).toBe('completed');
        expect(parseFilter('bogus')).toBe('all');
        expect(parseFilter(undefined)).toBe('all');
    });

    test('a filter selects by completion', () => {
        expect(matchesFilter('all', todo(true))).toBe(true);
        expect(matchesFilter('active', todo(true))).toBe(false);
        expect(matchesFilter('completed', todo(true))).toBe(true);
    });
});
```
