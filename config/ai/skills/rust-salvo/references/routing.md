# Routing

Salvo treats path, method and custom conditions uniformly as filters. Routers are values: build them in functions,
mount the same subtree under several prefixes, and attach middleware with `hoop()`. Module docs of
`salvo_core::routing` (docs.rs) are the upstream reference.

## Path syntax

`{name}` since 0.76. The old `<name>` form is not an error; it is matched as a literal and never matches.

```rust
Router::with_path("users")                       // static
Router::with_path("users/{id}")                  // one segment
Router::with_path("users/{id:num}")              // digits only (also i32, i64, u32, u64)
Router::with_path("users/{id:num(..=10)}")       // 1 to 10 digits; num[10], num(3..10), num(10..) too
Router::with_path(r"posts/{slug|[a-z0-9-]+}")    // regex for the segment
Router::with_path("images/{name}.{ext}")         // several parameters in one segment
Router::with_path("static/{**path}")             // the rest of the path, zero or more segments
Router::with_path("files/{*+path}")              // the rest of the path, one or more segments
Router::with_path("docs/{*?path}")               // zero or one segment
```

Verified on salvo 1.0.1 (`req.param::<String>(name)` shown):

| Route               | Request                       | Result                                              |
|---------------------|-------------------------------|-----------------------------------------------------|
| `many/{**p}`        | `/many`                       | 200, `p = ""`                                       |
| `many/{**p}`        | `/many/a/b`                   | 200, `p = "a/b"`                                    |
| `plus/{*+p}`        | `/plus`                       | 404                                                 |
| `plus/{*+p}`        | `/plus/a/b`                   | 200, `p = "a/b"`                                    |
| `opt/{*?p}`         | `/opt`                        | 200, `p = ""`                                       |
| `opt/{*?p}`         | `/opt/a/b`                    | 404                                                 |
| `one/{*p}`          | `/one/a/b`                    | 200, `p = "a/b"` (same as `{**p}`; not one segment) |
| `num/{id:num}`      | `/num/ab`                     | 404                                                 |
| `n3/{id:num(..=3)}` | `/n3/1234`                    | 404                                                 |
| `img/{id}.{p}`      | `/img/cat.png`                | 200, `id = "cat"`, `p = "png"`                      |
| `plain/{id}`        | `/plain/a%2Fb`                | 200, `id = "a/b"` (decoded)                         |
| `api/todos`         | `/api/todos/`, `//api//todos` | 200                                                 |

`{*name}` behaves like `{**name}` in 1.0.1 but is undocumented; write `{**name}`, `{*+name}` or `{*?name}`.
Parameter names must be unique within one path.

Read values with `req.param::<T>(name) -> Option<T>` (deserialized through serde) or the `PathParam<T>` extractor
(handlers reference). A constrained segment (`:num`, regex) never reaches the handler with a bad value, so
`unwrap()` there is safe; an unconstrained `{id}` with `PathParam<u64>` answers 400 for `abc`.

## Methods

```rust
Router::with_path("todos")
    .get(list).post(create)
    .push(Router::with_path("{id}").get(show).put(replace).patch(update).delete(remove));
```

`get`, `post`, `put`, `patch`, `delete`, `head` and `options` each add a child router filtered by that method.
`goal(h)` sets the handler without a method filter (WebSocket upgrades, proxies, catch-alls). A matched path with
no matching method answers 405; GET does not imply HEAD. OpenAPI skips routes without a method filter.

## Nesting and composition

```rust
fn todo_routes() -> Router {
    Router::with_path("todos")
        .get(list)
        .post(create)
        .push(Router::with_path("{id}").get(show).delete(remove))
}

let router = Router::new()
    .push(Router::with_path("api/v1").push(todo_routes()))
    .push(Router::with_path("api/v2").push(todo_routes()));
```

- `Router::with_path("a")` starts a new router; `router.path("a")` adds a path filter to an existing one.
- `Router::with_hoop(h)` starts a router with middleware: `Router::with_hoop(auth).path("admin")`.
- Nested paths concatenate: `with_path("api/v1")` then `with_path("users")` matches `/api/v1/users` only.
- `.unshift(router)` inserts a child first (used for doc routes); `.push` appends. Children are tried in order, so
  put specific routes before catch-alls like `{**path}`.
- `.then(|router| if cond { router.post(h) } else { router })` adds routes conditionally.

## Custom filters

```rust
Router::with_path("admin")
    .filter_fn(|req, _state| req.header::<String>("x-admin-key").is_some())
    .get(admin);
```

`salvo::routing::filters` has `path`, `get`/`post`/..., `host`, `port` and `scheme`. Combine them with `.and()` /
`.or()`, which need `use salvo::routing::Filter;` in scope. Implementing `Filter` by hand means advancing the
`PathState` cursor and inserting params yourself; prefer typed or regex params.

## Middleware scope

`hoop(h)` applies to the router and all its descendants, and runs only when a route below it matches; for an
unmatched path it does not run at all. `hoop_when(h, |req: &Request, depot: &Depot| bool)` adds it conditionally.
Service-wide middleware (logging, CORS for 404s, `ForceHttps`) goes on the `Service`.

```rust
let router = Router::new()
    .push(
        Router::with_path("api")
            .hoop(auth_check)                                // only /api/**
            .push(Router::with_path("users").get(list_users)),
    )
    .push(Router::with_path("public").get(public_page));     // no auth
let service = Service::new(router).hoop(Logger::new());      // every request, including 404s
```

## Redirects

```rust
res.render(Redirect::found("/tmp"));        // 302
res.render(Redirect::other("/page"));       // 303, after a POST
res.render(Redirect::temporary("/tmp"));    // 307
res.render(Redirect::permanent("/new"));    // 308
```

There is no `Redirect::see_other`; it is `other`.