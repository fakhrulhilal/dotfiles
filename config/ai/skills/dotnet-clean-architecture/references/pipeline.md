# Pipeline building blocks

Copy these into `Shop.Application/Common/` when starting a solution, and rename the namespace. They were built and run
against Mediator 3.0.2, FluentValidation 12.2.0 and ServiceScan.SourceGenerator 3.2.2 on .NET 10.

## Project file

Global usings are `<Using>` items, and only for namespaces from outside the project. FluentValidation ships its own
`ValidationException`, so the application's one is aliased for the whole project.

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

## DependencyInjection.cs

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

ServiceScan generates the body of both partial methods at build time: one `AddScoped` or `AddTransient` call per
type it finds in the project, nested and internal types included. Nothing is scanned at startup, so this also works
under Native AOT. Validators are scoped because they take repositories.

## Exceptions

`AuthenticationException` (not signed in) and `UnauthorizedAccessException` (signed in, not allowed) come from the
base class library.

```csharp
namespace Shop.Application.Common.Exceptions;

public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request is valid, but it could not be carried out in the current state.</summary>
public sealed class ConflictException(string message) : Exception(message);

public sealed class FeatureNotAvailableException(string message) : Exception(message);

public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation failures have occurred.") {
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
```

## Interfaces

```csharp
namespace Shop.Application.Common.Interfaces;

/// <summary>The signed-in user. Scoped, and it caches whatever it loads.</summary>
public interface ICurrentUser {
    int UserId { get; }
    int TenantId { get; }
    bool IsAdmin { get; }
    bool Can(Resource resource, Access access);
}

/// <summary>Changes a request after authorization and before validation, for example to sanitize HTML.</summary>
public interface IRequestFilter<in TRequest> {
    ValueTask PreValidate(TRequest request, CancellationToken cancellation);
}

/// <summary>A request about one order. <see cref="Order"/> is set by the pipeline once access is granted.</summary>
public interface IOrderRequest {
    int OrderId { get; }
    Order Order { set; }
}

public interface IEmailService {
    Task Send(int customerId, string subject, string body, CancellationToken cancellation);
}
```

## RequirePermissionAttribute

`Resource` and `Access` are enums in Domain.

```csharp
namespace Shop.Application.Common.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(Resource resource, params Access[] access) : Attribute {
    public Resource Resource { get; } = resource;
    public Access[] Access { get; } = access is { Length: > 0 }
        ? access
        : throw new ArgumentException("Require at least one access type.", nameof(access));
}
```

## UseCase

Messages are nested types named `Command` or `Query`, so their own type name says nothing. Logs use the enclosing
use case class instead.

```csharp
namespace Shop.Application.Common.Behaviors;

internal static class UseCase {
    /// <summary><c>CancelOrder</c> for <c>CancelOrder.Command</c>.</summary>
    public static string Of<TMessage>() => (typeof(TMessage).DeclaringType ?? typeof(TMessage)).Name;
}
```

## 1. LoggingBehavior

```csharp
using System.Security.Authentication;

using Microsoft.Extensions.Logging;

using Shop.Application.Common.Exceptions;

namespace Shop.Application.Common.Behaviors;

/// <summary>Outermost: logs what nobody expected and lets it bubble up to the host.</summary>
public sealed partial class LoggingBehavior<TMessage, TResponse>(ILogger<LoggingBehavior<TMessage, TResponse>> log)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage {
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellation) {
        try {
            return await next(message, cancellation);
        }
        catch (Exception exception) when (exception is not (ValidationException or NotFoundException
                     or ConflictException or FeatureNotAvailableException or UnauthorizedAccessException
                     or AuthenticationException or OperationCanceledException)) {
            Failed(log, exception, UseCase.Of<TMessage>());
            throw;
        }
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception while processing {Request}")]
    private static partial void Failed(ILogger log, Exception exception, string request);
}
```

## 2. AuthorizationBehavior

The attributes are read once per message type into a static field, so there is no reflection per request. They are
read from the message and from the use case class that encloses it. Reading only `typeof(TMessage)` would find
nothing on a nested message and let every caller through.

```csharp
using System.Reflection;
using System.Security.Authentication;

using Microsoft.Extensions.Logging;

using Shop.Application.Common.Attributes;
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Common.Behaviors;

/// <summary>Checks every <see cref="RequirePermissionAttribute"/> on the request against the current user.</summary>
public sealed partial class AuthorizationBehavior<TMessage, TResponse>(ICurrentUser user,
    ILogger<AuthorizationBehavior<TMessage, TResponse>> log)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage {
    private static readonly RequirePermissionAttribute[] Required = [
        .. typeof(TMessage).GetCustomAttributes<RequirePermissionAttribute>(),
        .. typeof(TMessage).DeclaringType?.GetCustomAttributes<RequirePermissionAttribute>() ?? []
    ];

    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellation) {
        if (Required.Length == 0) {
            return next(message, cancellation);
        }

        if (user.UserId == 0) {
            throw new AuthenticationException("Please login to get access.");
        }

        if (user.IsAdmin) {
            return next(message, cancellation);
        }

        foreach (var permission in Required) {
            var missing = permission.Access.Where(access => !user.Can(permission.Resource, access)).ToArray();
            if (missing.Length == 0) {
                continue;
            }

            string access = string.Join(", ", missing);
            Denied(log, user.UserId, UseCase.Of<TMessage>(), access, permission.Resource);
            throw new UnauthorizedAccessException($"You don't have access to {access} {permission.Resource}.");
        }

        return next(message, cancellation);
    }

    [LoggerMessage(LogLevel.Information, "User {UserId} invoked {Request} without {Access} access on {Resource}")]
    private static partial void Denied(ILogger log, int userId, string request, string access, Resource resource);
}
```

## 3. OrderAccessBehavior

One of these per aggregate that has per-entity access. The constraint keeps it out of every other message's pipeline.

```csharp
using System.Security.Authentication;

using Shop.Application.Common.Exceptions;
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Common.Behaviors;

/// <summary>
/// Runs only for <see cref="IOrderRequest"/>: the order must exist in the user's tenant and be theirs to touch.
/// The loaded order is handed to the request, so the validator and the handler don't query it again.
/// </summary>
public sealed class OrderAccessBehavior<TMessage, TResponse>(ICurrentUser user, IOrderRepository orders)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage, IOrderRequest {
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellation) {
        if (user.UserId == 0) {
            throw new AuthenticationException("Please login to get access.");
        }

        var order = await orders.Get(user.TenantId, message.OrderId, cancellation)
                    ?? throw new NotFoundException("Order is not found.");
        if (!user.IsAdmin && order.CustomerId != user.UserId) {
            throw new UnauthorizedAccessException("You don't have access to this order.");
        }

        message.Order = order;
        return await next(message, cancellation);
    }
}
```

## 4. FilterBehavior

```csharp
using Shop.Application.Common.Interfaces;

namespace Shop.Application.Common.Behaviors;

/// <summary>Runs the request's <see cref="IRequestFilter{TRequest}"/>, when it has one.</summary>
public sealed class FilterBehavior<TMessage, TResponse>(IEnumerable<IRequestFilter<TMessage>> filters)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage {
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellation) {
        foreach (var filter in filters) {
            await filter.PreValidate(message, cancellation);
        }

        return await next(message, cancellation);
    }
}
```

## 5. ValidationBehavior

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

using Shop.Application.Common.Exceptions;

namespace Shop.Application.Common.Behaviors;

/// <summary>Innermost: runs every validator of the request and stops the pipeline on the first failure.</summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage {
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellation) {
        var context = new ValidationContext<TMessage>(message);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators) {
            failures.AddRange((await validator.ValidateAsync(context, cancellation)).Errors);
        }

        if (failures.Count == 0) {
            return await next(message, cancellation);
        }

        if (failures.Find(failure => failure.ErrorCode == ValidationCodes.Forbidden) is { } forbidden) {
            throw new UnauthorizedAccessException(forbidden.ErrorMessage);
        }

        if (failures.Find(failure => failure.ErrorCode == ValidationCodes.NotFound) is { } notFound) {
            throw new NotFoundException(notFound.ErrorMessage);
        }

        throw new ValidationException(failures
            .GroupBy(failure => ValidationCodes.FieldName(failure.PropertyName), failure => failure.ErrorMessage)
            .ToDictionary(group => group.Key, group => group.ToArray()));
    }
}

/// <summary>Error codes a validator uses to answer 404 or 403 instead of 400.</summary>
public static partial class ValidationCodes {
    public const string NotFound = nameof(NotFound);
    public const string Forbidden = nameof(Forbidden);

    /// <summary><c>Lines[2]</c> becomes <c>lines</c>, so all errors of a collection share one key.</summary>
    internal static string FieldName(string property) =>
        JsonNamingPolicy.CamelCase.ConvertName(Index().Replace(property, string.Empty));

    [GeneratedRegex(@"\[\d+\]$")]
    private static partial Regex Index();
}
```

A rule that should answer 404 or 403 instead of 400:

```csharp
RuleFor(x => x.CouponId)
    .MustAsync(async (id, cancellation) => await coupons.Exists(id, cancellation))
    .WithMessage("Coupon is not found.").WithErrorCode(ValidationCodes.NotFound);
```

## Other pipeline types

Mediator also has `MessagePreProcessor<TMessage, TResponse>`, `MessagePostProcessor<TMessage, TResponse>` and
`MessageExceptionHandler<TMessage, TResponse, TException>`. They are registered in `PipelineBehaviors` like any
behavior. Use `MessageExceptionHandler` only to turn an exception into a response (`return Handled(response)`),
which needs `TResponse` constrained to something you can construct; to log and rethrow, a behavior with try/catch as
above is simpler.
