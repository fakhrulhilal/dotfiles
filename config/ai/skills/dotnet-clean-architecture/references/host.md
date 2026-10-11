# Host wiring

## Exception handler

```csharp
using System.Security.Authentication;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using Shop.Application.Common.Exceptions;

namespace Shop.Api;

/// <summary>Turns the exceptions of the application layer into RFC 9457 problem details.</summary>
public sealed class ApplicationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellation) {
        ProblemDetails? problem = exception switch {
            ValidationException validation => new HttpValidationProblemDetails(
                validation.Errors.ToDictionary(error => error.Key, error => error.Value)) {
                Status = StatusCodes.Status400BadRequest, Title = "Validation failed"
            },
            AuthenticationException => Problem(StatusCodes.Status401Unauthorized, "Unauthenticated"),
            UnauthorizedAccessException => Problem(StatusCodes.Status403Forbidden, "Forbidden"),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found"),
            ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict"),
            FeatureNotAvailableException => Problem(StatusCodes.Status409Conflict, "Feature not available"),
            _ => null
        };
        if (problem is null) {
            return false;
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new() { HttpContext = context, ProblemDetails = problem });

        ProblemDetails Problem(int status, string title) =>
            new() { Status = status, Title = title, Detail = exception.Message };
    }
}
```

Returning `false` leaves the exception to the default handler, which answers 500 without details.

## Program.cs

```csharp
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

var app = builder.Build();
app.UseExceptionHandler();
```

`ICurrentUser` is implemented in the host from `IHttpContextAccessor`, registered scoped, and caches what it loads
(user, tenant, permissions) for the request. `UserId` is `0` when nobody is signed in.

## Responses

- Commands: `204`, or `201`/`200` with the value of an `ICommand<T>`.
- Queries: `200` with the `Response`.
- Everything else is an exception and becomes a problem details response through the handler above, for example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation failed",
  "status": 400,
  "errors": {
    "orderId": ["You can only cancel an order that has not shipped."],
    "reason": ["'Reason' must not be empty."]
  }
}
```
