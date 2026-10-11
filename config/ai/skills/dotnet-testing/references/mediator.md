# Testing a mediator application layer

For an application layer built on a mediator (MediatR or a compatible library) with FluentValidation: one request has
a validator, a request handler and zero or more event handlers. Each part gets its own test class, and each class
isolates its part from the other two.

All rules from `SKILL.md` still apply. The examples use a `CreateOrder` command that publishes `OrderCreated`, which
`SendOrderConfirmation` handles.

## Layout

Tests mirror the feature folder of the request, one file per part:

```text
tests/Shop.Application.Tests/
  Testing.cs                      # Configure(): AddApplication() + every boundary mocked in its successful state
  Mediator.cs                     # isolation helpers below
  Mocks/
    OrderRepoMocks.cs
    EmailServiceMocks.cs
  Orders/Commands/CreateOrder/
    GivenCreateOrderValidator.cs
    GivenCreateOrderHandler.cs
    GivenSendOrderConfirmation.cs # one class per event handler, named after the handler
```

`Testing.Configure` registers the real application (`services.AddApplication()`: handlers, validators, pipeline
behaviors) and then mocks every boundary (repositories, current user, clock, email) in its successful state.

## Conventions shared by the three parts

- **Aliases.** Each file aliases its types, so tests read the same in every feature and long names don't wrap:
  `using Request = ...CreateOrderCommand;`, `using Event = ...OrderCreated;`, `using SUT = ...SendOrderConfirmation;`.
- **Valid input first.** The class has a private `ValidRequest()` or `ValidEvent()` that returns input passing every
  rule, with unique data. A test changes only what its scenario is about (`ValidRequest() with { Quantity = 0 }`).
- **Class `Setup`.** The class `Setup(scenario)` adds the isolation for its part and the defaults for its subject on
  top of `Configure`, then applies the test's scenario last so it wins.
- **Capture instead of verify.** State helpers take an optional callback (`x.OnSent(emails.Add)`,
  `x.Saves(orders.Add)`). The test collects what crossed the boundary into a local list and asserts on it.
- **Narrow by argument.** A state helper has an overload that answers only for a given argument
  (`x.Saves(tenantId)`). A subject that passes the wrong value gets the mock's default answer and the test fails,
  so no separate verification is needed.

| Part            | Subject                                 | Isolation in `Setup`                             | Act                                               |
|-----------------|-----------------------------------------|--------------------------------------------------|---------------------------------------------------|
| Validator       | `provider.GetValidator<Request>()`      | none, the real validator with mocked boundaries  | `await sut.ValidateAsync(request)`                |
| Request handler | `provider.Resolve<IMediator>()`         | `AlwaysValid<Request>()`, `ClearTriggers<Event>()` | `await sut.Send(request)`                       |
| Event handler   | `provider.ResolveHandler<SUT, Event>()` | `ClearTriggersExcept<SUT, Event>()`              | `await sut.Handle(@event, CancellationToken.None)` |

## Mediator.cs

These helpers need no mocking library, so they sit next to `Testing.cs`.

```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shop.Application.Tests;

public static class Mediator {
    public static IValidator<TRequest> GetValidator<TRequest>(this IServiceProvider provider) =>
        provider.Resolve<IValidator<TRequest>>();

    /// <summary>Replaces the validator with one that passes. Use it to test a request handler.</summary>
    public static IServiceCollection AlwaysValid<TRequest>(this IServiceCollection services) => services
        .RemoveAll<IValidator<TRequest>>()
        .AddSingleton<IValidator<TRequest>>(new InlineValidator<TRequest>());

    /// <summary>Replaces the validator with one that fails. Use it to test a pipeline behavior.</summary>
    public static IServiceCollection HasErrors<TRequest>(this IServiceCollection services,
        params ValidationFailure[] errors) {
        var validator = new InlineValidator<TRequest>();
        validator.RuleFor(request => request).Custom((_, context) => Array.ForEach(errors, context.AddFailure));
        return services.RemoveAll<IValidator<TRequest>>().AddSingleton<IValidator<TRequest>>(validator);
    }

    public static ValidationFailure Error(string property, string? message = null) =>
        new(property, message ?? $"error-{Guid.NewGuid():n}");

    /// <summary>Removes every handler of the event. Use it to test a request handler.</summary>
    public static IServiceCollection ClearTriggers<TEvent>(this IServiceCollection services)
        where TEvent : INotification => services.RemoveAll<INotificationHandler<TEvent>>();

    /// <summary>Removes every handler of the event but one. Use it to test that event handler.</summary>
    public static IServiceCollection ClearTriggersExcept<THandler, TEvent>(this IServiceCollection services)
        where TEvent : INotification where THandler : class, INotificationHandler<TEvent> {
        for (int index = services.Count - 1; index >= 0; index--) {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(INotificationHandler<TEvent>) &&
                descriptor.ImplementationType != typeof(THandler)) {
                services.RemoveAt(index);
            }
        }

        return services;
    }

    /// <summary>Captures the event published by the request handler.</summary>
    public static IServiceCollection OnTriggered<TEvent>(this IServiceCollection services, Action<TEvent> capture)
        where TEvent : INotification =>
        services.AddSingleton<INotificationHandler<TEvent>>(new Trigger<TEvent>(capture));

    public static THandler ResolveHandler<THandler, TEvent>(this IServiceProvider provider)
        where TEvent : INotification where THandler : class, INotificationHandler<TEvent> =>
        provider.GetServices<INotificationHandler<TEvent>>().OfType<THandler>().Single();

    public static void ShouldBeValid(this ValidationResult result) =>
        result.Errors.Select(error => error.ErrorMessage).Should().BeEmpty();

    public static void ShouldBeInvalid(this ValidationResult result, string property, string? message = null) {
        var messages = result.Errors
            .Where(error => error.PropertyName == property)
            .Select(error => error.ErrorMessage)
            .ToArray();
        messages.Should().NotBeEmpty($"'{property}' should have an error");
        if (message is not null) {
            messages.Should().Contain(message);
        }
    }
}

file sealed class Trigger<TEvent>(Action<TEvent> capture) : INotificationHandler<TEvent>
    where TEvent : INotification {
    public Task Handle(TEvent notification, CancellationToken cancellation) {
        capture(notification);
        return Task.CompletedTask;
    }
}
```

## Mocks with capture and argument overloads

```csharp
using FakeItEasy;

namespace Shop.Application.Tests.Mocks;

using Mocked = IOrderRepo;

public static class OrderRepoMocks {
    public static Mocked Saves(this Mocked mocked, Action<Order>? capture = null) {
        A.CallTo(() => mocked.Create(A<int>._, A<Order>._, A<CancellationToken>._))
            .Invokes((int _, Order order, CancellationToken _) => capture?.Invoke(order))
            .Returns(true);
        return mocked;
    }

    public static Mocked Saves(this Mocked mocked, int tenantId, Action<Order>? capture = null) {
        A.CallTo(() => mocked.Create(tenantId, A<Order>._, A<CancellationToken>._))
            .Invokes((int _, Order order, CancellationToken _) => capture?.Invoke(order))
            .Returns(true);
        return mocked;
    }

    public static Mocked FailsToSave(this Mocked mocked) {
        A.CallTo(() => mocked.Create(A<int>._, A<Order>._, A<CancellationToken>._)).Returns(false);
        return mocked;
    }
}
```

## Validator

Name tests after the rule: `When<Condition>Then<Property>IsInvalid`, and `When<Condition>ThenItIsValid` for the paths
that must pass. A validator that also loads data onto the request (to save the handler a second query) gets a test
for that too.

```csharp
using Request = Shop.Application.Orders.Commands.CreateOrder.CreateOrderCommand;

namespace Shop.Application.Tests.Orders.Commands.CreateOrder;

using static Testing;

public sealed class GivenCreateOrderValidator {
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task WhenQuantityIsNotPositiveThenQuantityIsInvalid(int quantity) {
        // arrange
        var request = ValidRequest() with { Quantity = quantity };
        var sut = Setup().GetValidator<Request>();

        // act
        var result = await sut.ValidateAsync(request);

        // assert
        result.ShouldBeInvalid(nameof(request.Quantity));
    }

    [Test]
    public async Task WhenCustomerIsBlockedThenCustomerIdIsInvalid() {
        // arrange
        var request = ValidRequest();
        var provider = Setup(services => services
            .Mock<ICustomerRepo>(x => x.Blocked(request.CustomerId)));
        var sut = provider.GetValidator<Request>();

        // act
        var result = await sut.ValidateAsync(request);

        // assert
        result.ShouldBeInvalid(nameof(request.CustomerId), "Customer is blocked.");
    }

    [Test]
    public async Task WhenRequestIsCompleteThenItIsValid() {
        // arrange
        var request = ValidRequest();
        var sut = Setup().GetValidator<Request>();

        // act
        var result = await sut.ValidateAsync(request);

        // assert
        result.ShouldBeValid();
    }

    private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Configure(services => {
        services.Mock<ICustomerRepo>(x => x.AlwaysActive());
        scenario?.Invoke(services);
    });

    private static Request ValidRequest() => new(CustomerId: Random.Shared.Next(1, int.MaxValue), Quantity: 2);
}
```

## Request handler

The subject is the mediator, so the request runs through the real pipeline. Validation is replaced by `AlwaysValid`
because the validator has its own class, and the event handlers are removed because each has its own class. Cover the
success result, each failure result, what was written, and the event that was published.

```csharp
using Request = Shop.Application.Orders.Commands.CreateOrder.CreateOrderCommand;

namespace Shop.Application.Tests.Orders.Commands.CreateOrder;

using static Testing;

public sealed class GivenCreateOrderHandler {
    [Test]
    public async Task WhenOrderIsSavedThenItSucceeds() {
        // arrange
        var orders = new List<Order>();
        var request = ValidRequest();
        var provider = Setup(services => services
            .Mock<IOrderRepo>(x => x.Saves(orders.Add)));
        var sut = provider.Resolve<IMediator>();

        // act
        var result = await sut.Send(request);

        // assert
        result.Succeeded.Should().BeTrue();
        orders.Should().ContainSingle().Which.CustomerId.Should().Be(request.CustomerId);
    }

    [Test]
    public async Task WhenSavingFailsThenItFails() {
        // arrange
        var provider = Setup(services => services
            .Mock<IOrderRepo>(x => x.FailsToSave()));
        var sut = provider.Resolve<IMediator>();

        // act
        var result = await sut.Send(ValidRequest());

        // assert
        result.Error.Should().Be("Unable to create the order.");
    }

    [Test]
    public async Task WhenOrderIsSavedThenItPublishesOrderCreated() {
        // arrange
        OrderCreated? published = null;
        var request = ValidRequest();
        var provider = Setup(services => services
            .OnTriggered<OrderCreated>(triggered => published = triggered));
        var sut = provider.Resolve<IMediator>();

        // act
        await sut.Send(request);

        // assert
        published.Should().BeEquivalentTo(new { request.CustomerId });
    }

    private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Configure(services => {
        services.AlwaysValid<Request>();
        services.ClearTriggers<OrderCreated>();
        services.Mock<IOrderRepo>(x => x.Saves());
        scenario?.Invoke(services);
    });

    private static Request ValidRequest() => new(CustomerId: Random.Shared.Next(1, int.MaxValue), Quantity: 2);
}
```

## Event handler

The subject is the handler itself, resolved from the container and called directly, so no request has to run first.
`ClearTriggersExcept` keeps the other handlers of the same event out of the test.

```csharp
using Event = Shop.Application.Orders.Events.OrderCreated;
using SUT = Shop.Application.Orders.Commands.CreateOrder.SendOrderConfirmation;

namespace Shop.Application.Tests.Orders.Commands.CreateOrder;

using static Testing;

public sealed class GivenSendOrderConfirmation {
    [Test]
    public async Task WhenCustomerOptedOutThenItSendsNothing() {
        // arrange
        var emails = new List<SentEmail>();
        var @event = ValidEvent() with { NotifyCustomer = false };
        var provider = Setup(services => services
            .Mock<IEmailService>(x => x.OnSent(emails.Add)));
        var sut = provider.ResolveHandler<SUT, Event>();

        // act
        await sut.Handle(@event, CancellationToken.None);

        // assert
        emails.Should().BeEmpty();
    }

    [Test]
    public async Task WhenOrderIsCreatedThenItEmailsTheCustomer() {
        // arrange
        var emails = new List<SentEmail>();
        var @event = ValidEvent();
        var provider = Setup(services => services
            .Mock<ICustomerRepo>(x => x.HasEmail(@event.CustomerId, "buyer@domain"))
            .Mock<IEmailService>(x => x.OnSent(emails.Add)));
        var sut = provider.ResolveHandler<SUT, Event>();

        // act
        await sut.Handle(@event, CancellationToken.None);

        // assert
        emails.Should().ContainSingle().Which.To.Should().Be("buyer@domain");
    }

    private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Configure(services => {
        services.ClearTriggersExcept<SUT, Event>();
        services.Mock<IEmailService>(x => x.SendsSuccessfully());
        scenario?.Invoke(services);
    });

    private static Event ValidEvent() =>
        new(OrderId: Random.Shared.Next(1, int.MaxValue), CustomerId: Random.Shared.Next(1, int.MaxValue),
            NotifyCustomer: true);
}
```

## Pipeline behaviors

Test a behavior once, in `Common/Behaviors/`, with a dummy request declared in the test file instead of a real
feature. Send it through the mediator, use `HasErrors<Request>(Error(nameof(Request.Name)))` or a mocked boundary to
drive the behavior, and assert on the exception or on whether the next step ran.
