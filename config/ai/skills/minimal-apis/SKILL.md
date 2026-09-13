---
name: minimal-apis
description: "Design and implement ASP.NET Core Minimal APIs, the default over controllers: one IEndpoint class per endpoint in feature folders, route groups composed in Endpoints.cs, endpoint filters exposed as RouteHandlerBuilder extensions, TypedResults, source-generated validation, Native AOT friendly JSON, and heavy startup work moved into BackgroundService. USE FOR: building or extending HTTP APIs in ASP.NET Core; organizing endpoints; adding filters or validation; moving migrations or seeding out of Program.cs. DO NOT USE FOR: existing controller-based projects (keep their style, see dotnet-webapi); unrelated stacks."
compatibility: "ASP.NET Core 8+, written for .NET 10."
---

# Minimal APIs

> This is a local fork of the `minimal-apis` skill from managedcode/dotnet-skills (see `../SOURCES.md`). Endpoint
> organization follows [fakhrulhilal/StructuredMinimalApi](https://github.com/fakhrulhilal/StructuredMinimalApi), and
> tests follow the `dotnet-testing` skill.

## Trigger On

- building or extending HTTP APIs in ASP.NET Core
- organizing endpoints into features and route groups
- implementing validation and filters
- running work at startup (migrations, seeding, warm-up)

## Documentation

- [Minimal APIs Overview](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
- [Filters in Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)
- [Route Groups](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#route-groups)
- [OpenAPI Support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)
- [Background tasks with hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)

### References

- [patterns.md](references/patterns.md): route groups, filters, TypedResults, parameter binding, error handling and
  testing
- [anti-patterns.md](references/anti-patterns.md): common Minimal API mistakes to avoid

## Minimal APIs vs controllers

- **New projects and new APIs: always Minimal APIs.**
- Existing controller-based projects keep controllers. Never mix both styles in one project; use `dotnet-webapi` there.

## Workflow

1. Create one class per endpoint that implements `IEndpoint`, in `<Feature>/Endpoints/<Action>.cs`.
2. The endpoint class owns its `Map` method, nested `Request`/`Response` records and a `private static Handle`.
3. `Endpoints.cs` composes feature groups (`/posts`, tags) and access groups (public or authorized), listing every endpoint
   explicitly. `Program.cs` only calls `app.MapEndpoints()`.
4. Expose cross-cutting behavior as endpoint filters wrapped in `RouteHandlerBuilder` extension methods, so `Map` reads
   like a sentence.
5. Return `TypedResults`, using `Results<...>` unions for multiple outcomes.
6. Validate with DataAnnotations plus `builder.Services.AddValidation()`, which is source-generated and AOT-compatible.
   Keep FluentValidation only in non-AOT projects that already use it.
7. Put heavy startup work in a dedicated `BackgroundService`, never in `Program.cs` (see `worker-services`).
8. Describe endpoints for OpenAPI with `.WithSummary()`, `.WithTags()` and `.Produces...()`.

## Organizing endpoints

### Layout

```text
src/
  Program.cs                       # builder.AddServices(); app.MapEndpoints();
  ConfigureServices.cs             # WebApplicationBuilder extensions
  Endpoints.cs                     # composes every feature group
  Common/Api/
    IEndpoint.cs
    Extensions/RouteHandlerBuilderExtensions.cs
    Filters/RequestLoggingFilter.cs
  Posts/Endpoints/
    CreatePost.cs
    GetPostById.cs
  Users/Endpoints/
    FollowUser.cs
```

### Contract

```csharp
namespace Chirper.Common.Api;

public interface IEndpoint {
    static abstract void Map(IEndpointRouteBuilder app);
}
```

### One endpoint

```csharp
namespace Chirper.Posts.Endpoints;

public sealed class GetPostById : IEndpoint {
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/{id:int}", Handle)
        .WithSummary("Gets a post by id");

    public sealed record Request([Range(1, int.MaxValue)] int Id);

    public sealed record Response(int Id, string Title, string? Content, DateTimeOffset CreatedAt);

    private static async Task<Results<Ok<Response>, NotFound>> Handle([AsParameters] Request request,
        IPostStore posts, CancellationToken cancellationToken) {
        var post = await posts.FindAsync(request.Id, cancellationToken);
        return post is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new Response(post.Id, post.Title, post.Content, post.CreatedAt));
    }
}
```

### Composition

```csharp
namespace Chirper;

public static class Endpoints {
    public static void MapEndpoints(this WebApplication app) {
        var endpoints = app.MapGroup("")
            .AddEndpointFilter<RequestLoggingFilter>();

        endpoints.MapPostEndpoints();
        endpoints.MapUserEndpoints();
    }

    private static void MapPostEndpoints(this IEndpointRouteBuilder app) {
        var endpoints = app.MapGroup("/posts")
            .WithTags("Posts");

        endpoints.MapPublicGroup()
            .MapEndpoint<GetPosts>()
            .MapEndpoint<GetPostById>();

        endpoints.MapAuthorizedGroup()
            .MapEndpoint<CreatePost>()
            .MapEndpoint<DeletePost>();
    }

    private static RouteGroupBuilder MapPublicGroup(this IEndpointRouteBuilder app, string? prefix = null) =>
        app.MapGroup(prefix ?? string.Empty).AllowAnonymous();

    private static RouteGroupBuilder MapAuthorizedGroup(this IEndpointRouteBuilder app, string? prefix = null) =>
        app.MapGroup(prefix ?? string.Empty).RequireAuthorization();

    private static IEndpointRouteBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app)
        where TEndpoint : IEndpoint {
        TEndpoint.Map(app);
        return app;
    }
}
```

### Rules

- List every endpoint explicitly in `Endpoints.cs`. Don't register endpoints by scanning assemblies with reflection: the
  `static abstract` contract plus the generic constraint resolves at compile time, keeping routes discoverable and
  trim/AOT safe.
- Each endpoint defines its own `Request` and `Response`. Don't share DTOs across endpoints, and don't expose storage
  entities.
- `Handle` is `private static`, and dependencies are handler parameters. Endpoint classes are never instantiated.
- Group-wide concerns (tags, authorization, rate limits, shared filters) go on the groups in `Endpoints.cs`. Concerns
  for a single endpoint go in that endpoint's `Map`.
- Wrap filters in `RouteHandlerBuilder` extensions that also declare the responses they can produce:

```csharp
namespace Chirper.Common.Api.Extensions;

public static class RouteHandlerBuilderExtensions {
    public static RouteHandlerBuilder WithEnsureEntityExists<TRequest, TEntity>(this RouteHandlerBuilder builder)
        where TRequest : IHasId where TEntity : class, IEntity => builder
        .AddEndpointFilter<EnsureEntityExistsFilter<TRequest, TEntity>>()
        .ProducesProblem(StatusCodes.Status404NotFound);
}

// usage in an endpoint
public static void Map(IEndpointRouteBuilder app) => app
    .MapDelete("/{id:int}", Handle)
    .WithSummary("Deletes a post")
    .WithEnsureEntityExists<Request, Post>();
```

### Native AOT

- Use `WebApplication.CreateSlimBuilder(args)` with `PublishAot`. The request delegate generator then produces the
  `Map*` handlers at compile time.
- Nested `Request`/`Response` records share simple names across endpoints. Give each one a unique
  `TypeInfoPropertyName` in the `JsonSerializerContext`, then put that context first in the resolver chain:

```csharp
[JsonSerializable(typeof(CreatePost.Request), TypeInfoPropertyName = "CreatePostRequest")]
[JsonSerializable(typeof(CreatePost.Response), TypeInfoPropertyName = "CreatePostResponse")]
[JsonSerializable(typeof(GetPostById.Response), TypeInfoPropertyName = "GetPostByIdResponse")]
internal sealed partial class WebOpts : JsonSerializerContext;

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WebOpts.Default));
```

## Startup work belongs in a BackgroundService

Hosted services start one after another, and the server doesn't accept requests until startup finishes. Don't migrate,
seed, warm caches or download remote metadata in `Program.cs` or `IHostedService.StartAsync`:

```csharp
// Don't: blocks startup, and the host can't report health meanwhile
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
app.Run();
```

Do: give each job its own `BackgroundService`, registered with `AddHostedService<T>()`:

```csharp
builder.Services.AddHostedService<DatabaseMigration>();

internal sealed class DatabaseMigration(IServiceScopeFactory scopeFactory, ILogger<DatabaseMigration> logger)
    : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(stoppingToken);
        logger.MigrationCompleted();
    }
}
```

- A hosted service has no scope by default, so create one with `IServiceScopeFactory.CreateAsyncScope()` to resolve
  scoped services.
- On .NET 10, `ExecuteAsync` runs on the thread pool, so it doesn't block startup. Keep the method thin and move the
  logic into a testable service.
- Pass `stoppingToken` to every call. Shutdown waits for `ExecuteAsync`, up to `HostOptions.ShutdownTimeout` (30 s by
  default).
- An unhandled exception stops the host by default (`BackgroundServiceExceptionBehavior.StopHost`). Catch what can be
  retried, and log it.
- When requests depend on the job, report completion through a readiness health check rather than blocking startup.
- For periodic work, use `PeriodicTimer` inside `ExecuteAsync`.

## Basic Patterns

### TypedResults (Strongly-Typed)

```csharp
app.MapGet("/products/{id}", Results<Ok<Product>, NotFound> (int id, AppDb db) =>
{
    var product = db.Products.Find(id);
    return product is not null
        ? TypedResults.Ok(product)
        : TypedResults.NotFound();
});
```

### Dependency Injection

```csharp
app.MapGet("/products", async (IProductService service) => await service.GetAllAsync());

// Or with [FromServices] for clarity
app.MapGet("/products", async ([FromServices] IProductService service) => await service.GetAllAsync());
```

## Route Groups

```csharp
var api = app.MapGroup("/api")
    .RequireAuthorization()
    .AddEndpointFilter<RequestLoggingFilter>();

var products = api.MapGroup("/products")
    .WithTags("Products");

var orders = api.MapGroup("/orders")
    .WithTags("Orders")
    .RequireAuthorization("AdminOnly");
```

## Endpoint Filters

### Inline Filter

```csharp
app.MapGet("/products/{id}", (int id) => Results.Ok(id))
    .AddEndpointFilter(async (context, next) =>
    {
        var id = context.GetArgument<int>(0);
        if (id <= 0)
            return Results.BadRequest("Invalid ID");

        return await next(context);
    });
```

### Class-Based Filter

```csharp
public sealed class RequestLoggingFilter(ILogger<RequestLoggingFilter> logger) : IEndpointFilter {
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        logger.RequestReceived(context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        return await next(context);
    }
}
```

### Validation

```csharp
builder.Services.AddValidation();

public sealed record Request([Required, MaxLength(100)] string Title, string? Content);
```

Invalid requests get `400 Bad Request` with validation problem details. Opt a single endpoint out with
`.DisableValidation()`. For complex rules, implement `IValidatableObject` or a custom `ValidationAttribute`.

## Anti-Patterns to Avoid

| Anti-Pattern                                  | Why It's Bad                                    | Better Approach                                   |
|-----------------------------------------------|-------------------------------------------------|---------------------------------------------------|
| Everything in Program.cs                      | Unmaintainable                                  | `IEndpoint` classes + `Endpoints.cs`               |
| Reflection-based endpoint scanning            | Hidden routes, breaks trimming/AOT              | Explicit `MapEndpoint<T>()` list                   |
| Shared request/response DTOs                  | Endpoints can't evolve independently            | Nested `Request`/`Response` per endpoint           |
| Manual validation in handlers                 | Error-prone, inconsistent responses             | DataAnnotations + `AddValidation()`                |
| Migrations or seeding before `app.Run()`      | Blocks startup, no health reporting             | Dedicated `BackgroundService`                      |
| Exposing entities                             | Tight coupling                                  | Endpoint-specific DTOs                             |
| No TypedResults                               | No compile-time checks, weak OpenAPI metadata   | `TypedResults` + `Results<...>`                    |
| Controllers in a new project                  | Heavier, reflection-based, mixed styles         | Minimal APIs                                       |

## OpenAPI Integration

```csharp
builder.Services.AddOpenApi();

app.MapOpenApi();  // Serves OpenAPI spec

app.MapGet("/products", GetProducts)
    .WithName("GetProducts")
    .WithSummary("Get all products")
    .WithDescription("Returns a list of all available products")
    .Produces<List<Product>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError);
```

## Testing

Follow `dotnet-testing`: share one `WebApplicationFactory<Program>` per test session, override only the fakes a scenario
needs, and keep arrange/act/assert blocks. See the Testing Patterns section of [patterns.md](references/patterns.md).

## Deliver

- one `IEndpoint` class per endpoint, composed explicitly in `Endpoints.cs`
- route groups for features and access levels
- type-safe responses with TypedResults
- source-generated validation and JSON
- startup work in dedicated background services
- OpenAPI metadata

## Validate

- every endpoint is reachable through `Endpoints.cs`, and nothing is registered via reflection
- endpoints return the correct status codes, and validation rejects invalid input
- the AOT publish produces no trim/AOT warnings
- the app starts serving (and `/health` responds) while background startup jobs are still running
- tests follow `dotnet-testing`