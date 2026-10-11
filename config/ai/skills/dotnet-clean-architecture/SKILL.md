---
name: dotnet-clean-architecture
description: "Build or extend a .NET clean architecture solution on the Mediator source generator (martinothamar/Mediator, not MediatR): Domain, Application, Persistence, Infrastructure and Api layers; one file per use case, a static class whose nested types are the command or query, its handler, its FluentValidation validator and its optional filter; one file per event handler under the event; events published after a successful command; a fixed pipeline of behaviors (unhandled-exception logging, attribute-based authorization, resource access that loads the entity, request filters, validation); every failure thrown as one of a small set of exceptions that the host maps to problem details (no `Result` type); mutable command classes and a `Response` record per query. Use when starting such a solution, adding a command, query, request handler, event or event handler, adding or ordering a pipeline behavior, wiring validation, authorization or exception handling into the mediator, or moving a project from MediatR to Mediator."
license: MIT
compatibility: "Mediator 3.x, FluentValidation 12, ServiceScan.SourceGenerator 3.x, written for .NET 10."
---

# .NET clean architecture on Mediator

The mediator is [martinothamar/Mediator](https://github.com/martinothamar/Mediator): a source generator, so there is no
reflection at dispatch, missing or duplicate handlers are build errors, and it works under Native AOT. Its API looks
like MediatR's but is not the same (see [Coming from MediatR](#coming-from-mediatr)).

Related skills: `minimal-apis` for the endpoints, `microsoft-extensions` for DI and options, `modern-csharp` for
language features, `dotnet-testing` for tests of validators, request handlers and event handlers.

## Layers

```text
src/
  Shop.Domain/           # entities, enums, repository interfaces. References nothing
  Shop.Application/      # use cases: messages, handlers, validators, behaviors. References Domain
  Shop.Persistence/      # repository implementations. References Domain
  Shop.Infrastructure/   # email, storage, clock, tokens. References Application
  Shop.Api/              # endpoints, ICurrentUser, exception handler. References all, composes them
```

- Dependencies point inward. Application never references Persistence, Infrastructure or ASP.NET Core.
- Each layer exposes one `Add<Layer>()` extension in a `DependencyInjection.cs`, declared in the
  `Microsoft.Extensions.DependencyInjection` namespace so the host needs no extra `using`.
- Interfaces live with the layer that uses them: repositories in Domain, `ICurrentUser` and `IEmailService` in
  Application. Time is `TimeProvider`.

## Packages

Application is the only project that references the generator, and `AddApplication()` is the only place that calls
`AddMediator`. Every message, handler and behavior therefore lives in Application. Installing the generator in a
second project of the same app generates a second mediator and fails.

```xml
<ItemGroup>
  <PackageReference Include="Mediator.Abstractions" Version="3.0.2" />
  <PackageReference Include="Mediator.SourceGenerator" Version="3.0.2" PrivateAssets="all" />
  <PackageReference Include="FluentValidation" Version="12.2.0" />
  <PackageReference Include="ServiceScan.SourceGenerator" Version="3.2.2" PrivateAssets="all" />
  <InternalsVisibleTo Include="$(AssemblyName).Tests" />
</ItemGroup>

<ItemGroup>
  <Using Include="FluentValidation" />
  <Using Include="Mediator" />
  <Using Include="Shop.Domain" />
  <Using Include="Shop.Application.Common.Exceptions.ValidationException" Alias="ValidationException" />
</ItemGroup>
```

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;

using ServiceScan.SourceGenerator;

using Shop.Application.Common.Behaviors;
using Shop.Application.Common.Interfaces;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static partial class DependencyInjection {
    public static IServiceCollection AddApplication(this IServiceCollection services) {
        services.AddMediator(options => {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(OrderAccessBehavior<,>),
                typeof(FilterBehavior<,>),
                typeof(ValidationBehavior<,>)
            ];
        });
        services.AddValidators().AddRequestFilters();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    [GenerateServiceRegistrations(AssignableTo = typeof(IValidator<>), Lifetime = ServiceLifetime.Scoped)]
    private static partial IServiceCollection AddValidators(this IServiceCollection services);

    [GenerateServiceRegistrations(AssignableTo = typeof(IRequestFilter<>), Lifetime = ServiceLifetime.Transient)]
    private static partial IServiceCollection AddRequestFilters(this IServiceCollection services);
}
```

- The `AddMediator` options are read by the generator at build time. Write them inline with `typeof(...)` and
  constants, never from a variable or configuration.
- Lifetime is `Scoped` because handlers and behaviors take the scoped `ICurrentUser` and repositories. Mediator's
  default is `Singleton`, which would capture those scoped services for the life of the app.
- Behaviors must be listed in `PipelineBehaviors`. They are never discovered, and the array order is the run order.
- Validators and filters are registered by
  [ServiceScan.SourceGenerator](https://github.com/Dreamescaper/ServiceScan.SourceGenerator), which writes the
  `AddScoped`/`AddTransient` calls at build time. Don't scan assemblies by reflection (Scrutor,
  `AddValidatorsFromAssembly`): it costs startup time and breaks under Native AOT.
- Global usings are `<Using>` items in the project file, not a `Usings.cs`, and only for namespaces from outside the
  project that most files need (packages and referenced projects). The project's own namespaces are imported with a
  normal `using` in each file. The one alias is there because FluentValidation ships its own `ValidationException`.

## Feature layout

```text
Shop.Application/
  DependencyInjection.cs
  Common/
    Attributes/    # RequirePermissionAttribute
    Behaviors/     # the pipeline
    Exceptions/    # NotFoundException, ConflictException, FeatureNotAvailableException, ValidationException
    Interfaces/    # ICurrentUser, IRequestFilter<T>, IOrderRequest, IEmailService
  Orders/
    Commands/
      CancelOrder.cs                     # Command, Handler, Validator and optional Filter
    Queries/
      GetOrders.cs                       # Query, Response and Handler
      GetOrderDetails.cs
    Events/
      OrderCancelled.cs
      OrderCancelledHandlers/            # one file per event handler, an event can have several
        SendEmailNotification.cs
        WriteAudit.cs
```

Name the feature folder in the plural (`Orders`). A folder named `Order` makes a namespace `Shop.Application.Order`
that hides the `Order` entity in every file below `Shop.Application`.

## Rules

1. **One file per use case.** A `public static class` named after the use case holds its parts as nested types:
   the message, `Handler`, `Validator`, the optional `Filter`, and for a query its `Response`. The message and
   `Response` are `public`, the rest are `internal sealed`. Nothing nested in a use case is used by another use
   case; shared code moves to `Common/` or to the feature root.
2. **Name by intent.** The use case is `<Verb><Noun>` (`CancelOrder`, `GetOrders`) and its nested types are always
   `Command` or `Query`, `Response`, `Handler`, `Validator` and `Filter`, so callers write
   `new CancelOrder.Command { ... }`. Never name a response `<Something>Vm` or `<Something>Dto`. Never
   name a namespace or folder after a use case; it would clash with the class. Events are past tense without a
   suffix (`OrderCancelled`). Event handlers are named after what they do (`SendEmailNotification`, `WriteAudit`),
   not after the event.
3. **Commands and queries.** A command changes state and a query never does.
   - A `Command` is an ordinary `sealed class` with `{ get; set; }` properties, never a record: the pipeline and
     the filter change it on the way to the handler. It implements `ICommand`, or `ICommand<T>` only when the
     caller needs a value back, such as the id of what was created. Its handler implements
     `ICommandHandler<Command>` and returns `ValueTask<Unit>` (`Unit.Value`), or `ICommandHandler<Command, T>`.
   - A `Query` implements `IQuery<Response>` or a collection of it. `Response` is always a nested `record`. The
     handler implements `IQueryHandler<Query, Response>`.
4. **Declare access on the use case.** Permissions are `[RequirePermission]` attributes on the use case class (or on
   the message itself), and access to one entity is an interface on the message such as `IOrderRequest`. A handler
   never checks permissions itself.
5. **Internal context.** What the pipeline or the validator had to load anyway (the order, the rows a rule checked)
   is stored on the message in an `Internal context` region, and the handler reads it from there instead of querying
   again. Mark those properties `[JsonIgnore]`. One that implements an interface member (`IOrderRequest.Order`) has
   to be `public`; the others are `internal`.
6. **Handlers hold the use case and nothing else.** No validation, no authorization, no try/catch for logging, no
   HTTP types. A handler takes repositories and services, does the work, and returns.
7. **Throw on failure.** There is no `Result` type and no success flag. A handler that returns has succeeded;
   anything else throws one of the exceptions in `Common/Exceptions`, and the host turns it into a response. Use
   `ConflictException` when the request is valid but could not be carried out (could not save, state changed in
   between). Don't catch exceptions in a handler to turn them into return values.
8. **Side effects are events.** After the state change succeeds, the handler publishes one event through
   `IPublisher`, and each side effect (email, audit, notification) is its own `INotificationHandler<TEvent>` file in
   `Events/<Event>Handlers/`. An event is a `sealed record` carrying everything its handlers need, so
   they don't load it again. A handler never calls another command.
9. **Event handlers stand alone.** One must not depend on another having run, and each returns early when it has
   nothing to do. With the default `ForeachAwaitPublisher` they run one by one, and an exception in one stops the
   rest and fails the command.
10. **Validators may use repositories.** Rules that need data are `MustAsync` rules with injected repositories.
    Answer 404 or 403 from a rule with `.WithErrorCode(ValidationCodes.NotFound)` or `ValidationCodes.Forbidden`.
11. **Map by hand.** A `Response` has a static `Map(entity)`. No mapping library.

## A command

```csharp
using System.Text.Json.Serialization;

using Shop.Application.Common.Attributes;
using Shop.Application.Common.Exceptions;
using Shop.Application.Common.Interfaces;
using Shop.Application.Orders.Events;

namespace Shop.Application.Orders.Commands;

[RequirePermission(Resource.Order, Access.Edit)]
public static class CancelOrder {
    public sealed class Command : ICommand, IOrderRequest {
        public int OrderId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool NotifyCustomer { get; set; } = true;

        #region Internal context

        /// <summary>Set by <c>OrderAccessBehavior</c>.</summary>
        [JsonIgnore]
        public Order Order { get; set; } = null!;

        #endregion
    }

    internal sealed class Handler(IOrderRepository orders, ICurrentUser user, IPublisher publisher)
        : ICommandHandler<Command> {
        public async ValueTask<Unit> Handle(Command request, CancellationToken cancellation) {
            if (!await orders.Cancel(user.TenantId, request.OrderId, request.Reason, cancellation)) {
                throw new ConflictException("Unable to cancel the order.");
            }

            await publisher.Publish(new OrderCancelled(request.Order, request.Reason, request.NotifyCustomer),
                cancellation);
            return Unit.Value;
        }
    }

    internal sealed class Filter : IRequestFilter<Command> {
        public ValueTask PreValidate(Command request, CancellationToken cancellation) {
            request.Reason = request.Reason.Trim();
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class Validator : AbstractValidator<Command> {
        public Validator() {
            RuleFor(x => x.OrderId)
                .Must((command, _) => command.Order.State == OrderState.Placed)
                .WithMessage("You can only cancel an order that has not shipped.");
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        }
    }
}
```

The filter is optional: at most one `IRequestFilter<TRequest>` per request, for changes that must happen before
validation (trimming, HTML sanitizing).

The nested `Handler`, `Validator` and `Filter` need no registration of their own: Mediator's generator picks up the
handler, and ServiceScan's generator registers the validator and the filter.

`command.Order` is already set here, because resource access runs before validation.

## Event and event handlers

```csharp
namespace Shop.Application.Orders.Events;

public sealed record OrderCancelled(Order Order, string Reason, bool NotifyCustomer) : INotification;
```

```csharp
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Orders.Events.OrderCancelledHandlers;

internal sealed class SendEmailNotification(IEmailService email) : INotificationHandler<OrderCancelled> {
    public async ValueTask Handle(OrderCancelled @event, CancellationToken cancellation) {
        if (!@event.NotifyCustomer) {
            return;
        }

        await email.Send(@event.Order.CustomerId, "Your order was cancelled", @event.Reason, cancellation);
    }
}
```

```csharp
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Orders.Events.OrderCancelledHandlers;

internal sealed class WriteAudit(IAuditRepository audits, ICurrentUser user, TimeProvider clock)
    : INotificationHandler<OrderCancelled> {
    public async ValueTask Handle(OrderCancelled @event, CancellationToken cancellation) =>
        await audits.Add(user.TenantId, user.UserId, $"Order {@event.Order.OrderId} cancelled: {@event.Reason}",
            clock.GetUtcNow(), cancellation);
}
```

## A query

```csharp
using Shop.Application.Common.Attributes;
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Orders.Queries;

[RequirePermission(Resource.Order, Access.View)]
public static class GetOrders {
    public sealed record Query(OrderState? State = null) : IQuery<IReadOnlyList<Response>>;

    public sealed record Response(int OrderId, string State) {
        public static Response Map(Order order) => new(order.OrderId, order.State.ToString());
    }

    internal sealed class Handler(IOrderRepository orders, ICurrentUser user)
        : IQueryHandler<Query, IReadOnlyList<Response>> {
        public async ValueTask<IReadOnlyList<Response>> Handle(Query request, CancellationToken cancellation) {
            var found = await orders.List(user.TenantId, request.State, cancellation);
            return [.. found.Select(Response.Map)];
        }
    }
}
```

## Pipeline

Every message sent through `ISender` passes the behaviors in this order. Each one either calls `next` or throws.

| Order | Behavior                | Runs for                         | Does                                                            | Throws                                                     |
|-------|-------------------------|----------------------------------|-----------------------------------------------------------------|------------------------------------------------------------|
| 1     | `LoggingBehavior`       | every message                    | logs exceptions nobody expected, then rethrows                  | nothing of its own                                         |
| 2     | `AuthorizationBehavior` | use cases with `[RequirePermission]` | checks the signed-in user against each attribute, read from the message and its enclosing class | `AuthenticationException`, `UnauthorizedAccessException`   |
| 3     | `OrderAccessBehavior`   | messages implementing `IOrderRequest` | loads the order in the user's tenant, checks ownership, sets `message.Order` | `NotFoundException`, `UnauthorizedAccessException` |
| 4     | `FilterBehavior`        | messages with an `IRequestFilter<T>` | lets the filter change the message                          | nothing of its own                                         |
| 5     | `ValidationBehavior`    | messages with validators         | runs every `IValidator<T>`                                      | `ValidationException`, or 404/403 by error code            |

- A behavior is `IPipelineBehavior<TMessage, TResponse>` with a `where TMessage : IMessage` constraint. Narrow the
  constraint (`where TMessage : IMessage, IOrderRequest`) and the generator applies it only to matching messages.
- Add a resource behavior per aggregate that needs one (`IOrderRequest` + `OrderAccessBehavior`), between
  authorization and filters.
- Notifications don't pass the pipeline. Only `Send` does.

Read [references/pipeline.md](references/pipeline.md) for the code of all five behaviors, the attribute, the
exceptions and the filter registration. Copy them when starting a solution.

## Host

The host maps the application's exceptions to RFC 9457 problem details in one `IExceptionHandler`, and endpoints
only translate between HTTP and messages.

| Exception                      | Status |
|--------------------------------|--------|
| `ValidationException`          | 400, with `errors` keyed by camelCase property |
| `AuthenticationException`      | 401    |
| `UnauthorizedAccessException`  | 403    |
| `NotFoundException`            | 404    |
| `ConflictException`            | 409    |
| `FeatureNotAvailableException` | 409    |
| anything else                  | 500 from the default handler, already logged by `LoggingBehavior` |

```csharp
app.MapPost("/orders/{orderId:int}/cancel",
    async Task<NoContent> (int orderId, CancelOrderRequest body, ISender sender, CancellationToken cancellation) => {
        await sender.Send(new CancelOrder.Command { OrderId = orderId, Reason = body.Reason }, cancellation);
        return TypedResults.NoContent();
    });
```

Inject `ISender` in endpoints and `IPublisher` in handlers, not `IMediator`. Read
[references/host.md](references/host.md) for the exception handler and its registration.

## Coming from MediatR

| MediatR                                             | Mediator                                                              |
|-----------------------------------------------------|-----------------------------------------------------------------------|
| `Task<T> Handle(...)`                               | `ValueTask<T> Handle(...)`, also for notification handlers            |
| `IRequest<T>` for everything                        | `ICommand<T>` and `IQuery<T>` (`IRequest<T>` still exists)            |
| `RequestHandlerDelegate<T> next`, called as `next()` | `MessageHandlerDelegate<TMessage, T> next`, called as `next(message, cancellation)` |
| `where TRequest : IRequest<TResponse>`              | `where TMessage : IMessage`                                           |
| `cfg.AddOpenBehavior(typeof(X<,>))`                 | `options.PipelineBehaviors = [typeof(X<,>)]`                          |
| `cfg.RegisterServicesFromAssembly(...)`             | nothing; handlers are found at build time                             |
| transient by default                                | singleton by default; set `ServiceLifetime.Scoped`                    |
| custom `INotificationPublisher` over executors      | `options.NotificationPublisherType`: `ForeachAwaitPublisher` (default) or `TaskWhenAllPublisher` |
| generic requests and notifications                  | not supported                                                         |

## Checklist for a new use case

1. Create `<Feature>/Commands/<UseCase>.cs` or `<Feature>/Queries/<UseCase>.cs` with a static class of that name.
2. Put the permission attributes on the class, then nest the message with its resource interface and the `Handler`.
3. Nest the `Validator`. Nest a `Filter` only when input must change before validation.
4. For side effects, add the event under `<Feature>/Events/` and one handler file per effect in
   `<Feature>/Events/<Event>Handlers/`.
5. Add the endpoint in the host (`minimal-apis`).
6. Add one test class each for the validator, the handler and every event handler (`dotnet-testing`).
7. Build. A missing handler or two handlers for one message is a build error from the generator.
