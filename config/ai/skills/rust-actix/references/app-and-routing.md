# App, routing and state

From the actix.rs pages Application, URL Dispatch and Extractors (state), and the `App`, `Scope`, `Resource`,
`Route` and `guard` docs. Behaviour marked *(run)* was checked against actix-web 4.18.1.

## Building blocks

| Item                         | What it is                                                                                   |
|------------------------------|----------------------------------------------------------------------------------------------|
| `App::new()`                 | The application of one worker. Holds app data, services, default service, middleware.        |
| `web::resource("/path/{id}")`| One path pattern with any number of routes; `.name("x")` for `url_for`.                      |
| `web::route()`               | A route: guards plus one handler (`.to(handler)`). `web::get()` = `route().guard(Get())`, likewise `post`, `put`, `patch`, `delete`, `head`, `method(Method)`. |
| `web::scope("/api")`         | A prefix. Holds resources, nested scopes, its own app data, middleware and default service.  |
| `App::route(path, route)`    | Shortcut for a resource with one route. Several calls with the same path add routes to separate resources, so prefer `web::resource(..).route(..).route(..)`. |
| `web::to(handler)`           | A route without guards: matches every method. Used for `default_service`.                    |
| `App::configure(f)` / `Scope::configure(f)` | `f(&mut web::ServiceConfig)` registers services, routes and app data: the way to split routing over modules. |
| `#[get("/path")] async fn x()` | Routing macro: makes `x` a service; register with `App::service(x)`. A handler with a macro cannot be called directly (unit tests). |

```rust
pub fn routes(config: &mut web::ServiceConfig) {
    config.service(
        web::resource("/todos")
            .route(web::get().to(list))
            .route(web::post().to(create)),
    );
    config.service(web::resource("/todos/{id}").route(web::get().to(read)));
}

App::new().service(web::scope("/api/v1").configure(routes));
```

## Matching

- Services are tried in registration order; the first resource whose pattern matches and has a route whose guards
  all pass handles the request.
- A scope matches by prefix at a segment boundary: scope `/app` matches `/app`, `/app/` and `/app/x`, not
  `/application`. Register more specific scopes before shorter prefixes.
- Patterns match the path exactly *(run)*: `/todos/` does not match `/todos`, `//todos` matches nothing, and a scope
  `/s` with only `/x` inside answers 404 for `/s`. `middleware::NormalizePath::trim()` rewrites the path before
  routing (trims the trailing slash *and* merges `//`) *(run)*; `NormalizePath::new(TrailingSlash::Always)` appends
  one. Rewriting is internal: no redirect, and POST keeps its body.
- A path that matches a resource, but whose routes all fail their guards, gets the resource's default service:
  405 with `Allow` listing the methods its method guards checked, empty body *(run: `allow: GET, POST`)*. Override
  per resource with `.default_service(..)`.
- A path that matches nothing gets the scope's default service if it has one, otherwise the app's
  (`App::default_service`, default: 404, empty body *(run)*).
- Method guards are exact: `web::get()` does not match HEAD *(run: 405)*. For GET + HEAD:
  `web::route().guard(guard::Any(guard::Get()).or(guard::Head()))`. actix-http drops the body of HEAD responses and
  keeps the headers.

## Path patterns

| Pattern                | Matches                                                                 |
|------------------------|-------------------------------------------------------------------------|
| `/users/{id}`          | one segment, `[^/]+`; the value is percent-decoded                     |
| `/files/{name}.{ext}`  | parts of a segment, split at literal text                              |
| `/items/{id:\d+}`      | a segment that matches the regex (no match = the next resource / 404) |
| `/static/{tail:.*}`    | the rest of the path, slashes included (empty allowed)                 |

Read values with the `Path<T>` extractor (tuple in order, or a `Deserialize` struct by name), or
`req.match_info().get("id")` / `.query("id")`. A segment marker needs at least one character: `/abc/{foo}` does not
match `/abc/`.

Never pass a `{tail:.*}` value straight to the file system: `..` segments reach outside the folder. Use
`actix_files::Files` for directories.

## Guards

A guard is `Fn(&GuardContext) -> bool` or a `Guard` impl. Built-ins: `guard::Get()`, `Post()`, ... `Method(m)`,
`Header("name", "value")`, `Host("example.com")`, `Acceptable::new(mime)`, `Not(g)`, `Any(g).or(g)`, `All(g).and(g)`.

```rust
web::route().guard(guard::fn_guard(|ctx| ctx.head().headers().contains_key("x-api-key"))).to(handler)
```

Guards cannot modify the request; to pass data on, insert into `ctx.req_data_mut()` (request extensions). Guards on
`App::service(web::scope(..).guard(guard::Host("api.example.com")))` give virtual hosting.

## URL generation

Name a resource and build its URL from a request:

```rust
web::resource("/todos/{id}").name("todo").route(web::get().to(read));
let url = req.url_for("todo", [id.to_string()])?; // Url: scheme and host from the request (or ConnectionInfo)
let path_only = req.url_for("todo", [id.to_string()])?.path().to_owned();
req.url_for_static("index")?; // no parameters
```

`App::external_resource("youtube", "https://youtube.com/watch/{video_id}")` registers a name for URL generation
only.

## Application state

| Item                        | Use                                                                                         |
|-----------------------------|---------------------------------------------------------------------------------------------|
| `web::Data<T>`              | `Arc<T>` behind an extractor. Register with `.app_data(Data::new(t))` (or `Data::from(arc)`), extract `Data<T>`. Missing or of another type: 500 "Requested application data is not configured correctly" *(run)*. |
| `web::ThinData<T>`          | For a `T` that is already cheap to clone (a pool handle): registered and extracted as `ThinData<T>`, no extra `Arc`. |
| `.app_data(T)` + `req.app_data::<T>()` | Any `'static` value, looked up by type, nearest scope first. Extractor configs (`JsonConfig`, ...) are app data too. |
| `App::data_factory(async fn)` | Builds per-worker state asynchronously when the worker starts.                           |

The factory closure passed to `HttpServer::new` runs on every worker. State made **inside** it is per worker (no
locking needed, counts differ between workers); state made **outside** it and cloned in is shared:

```rust
let shared = web::Data::new(AppState::new(pool));   // once
HttpServer::new(move || App::new().app_data(shared.clone()) /* ... */)
```

Mutable shared state needs interior mutability (`Mutex`, `RwLock`, atomics). Keep critical sections short and never
hold a `std::sync` guard across `.await`.

## Putting the app in one function

Share the app between `main` and tests by returning `App<impl ServiceFactory<..>>`:

```rust
pub fn app(state: Data<AppState>, settings: Settings) -> App<
    impl ServiceFactory<ServiceRequest, Config = (), Response = ServiceResponse<impl MessageBody>, Error = Error, InitError = ()>,
> {
    App::new().app_data(state).configure(api::routes).wrap(Logger::default())
}
```

Take owned arguments. With a borrowed one (`settings: &Settings`) the edition 2024 `impl` return type captures the
borrow and `HttpServer::new(move || app(state.clone(), &settings))` fails with "lifetime may not live long enough";
`+ use<>` does not help because the nested `impl MessageBody` captures it too.
