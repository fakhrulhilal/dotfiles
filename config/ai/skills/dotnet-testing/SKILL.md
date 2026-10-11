---
name: dotnet-testing
description: "House style for .NET tests: TUnit on Microsoft.Testing.Platform, Given/When/Then naming, AAA pattern with `// arrange`, `// act`, `// assert` comments, AwesomeAssertions `Should()` assertions (never FluentAssertions), layered happy-flow setup (assembly Testing.cs, then class Setup, then per-test scenario), and mocks hidden behind intent-named `Mocks`/`Mocked`/`Mock()` helpers whatever the library underneath (FakeItEasy preferred, NSubstitute for new projects otherwise, Moq only where it is already used). Covers mediator application layers: validators, request handlers and event handlers. Use when creating a test project, writing or reviewing tests, adding mocks, testing a MediatR or FluentValidation request, or migrating tests from xUnit, NUnit or MSTest."
license: MIT
---

# .NET testing style

This style is adapted from the tests in
[fakhrulhilal/cleanarchitecture-kit](https://github.com/fakhrulhilal/cleanarchitecture-kit/tree/master/tests), moved from
NUnit and Moq to TUnit. Framework mechanics are in `tunit`; commands are in `run-tests` and `filter-syntax`; moving an
old project over is covered by `migrate-vstest-to-mtp`.

## Stack

| Concern    | Choice                                                                                                                  |
|------------|-------------------------------------------------------------------------------------------------------------------------|
| Framework  | TUnit for every new test project. Existing xUnit/NUnit/MSTest projects stay unless a migration is requested, and then migrate to TUnit |
| Runner     | Microsoft.Testing.Platform (MTP) at minimum. Never add `Microsoft.NET.Test.Sdk`, `coverlet.*` or VSTest adapters to a TUnit project |
| Mocks      | FakeItEasy. NSubstitute for a new project where FakeItEasy doesn't fit. Moq only in a project that already uses it     |
| Assertions | AwesomeAssertions, the Apache-2.0 community fork of FluentAssertions. Never add `FluentAssertions`; a project that has it moves over by swapping the package and the `FluentAssertions` namespace for `AwesomeAssertions` |
| Time       | Inject `TimeProvider`; tests use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`)                       |
| Web        | `WebApplicationFactory<Program>` from `Microsoft.AspNetCore.Mvc.Testing`                                                |

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" Version="1.73.19" />
    <PackageReference Include="AwesomeAssertions" Version="9.6.0" />
    <PackageReference Include="FakeItEasy" Version="9.0.1" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="AwesomeAssertions" />
  </ItemGroup>

</Project>
```

`global.json` next to the solution or test folder, so `dotnet test` runs in native MTP mode on the .NET 10 SDK:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

```shell
dotnet test --project tests/App.Tests/App.Tests.csproj
dotnet test --project tests/App.Tests/App.Tests.csproj --treenode-filter "/*/*/GivenMailSender/*"
dotnet test --project tests/App.Tests/App.Tests.csproj --coverage --coverage-output-format cobertura
```

## Layout

```text
tests/App.Tests/
  App.Tests.csproj
  Testing.cs                  # assembly happy flow + readability helpers
  Mocks/                      # the only folder that references the mocking library
    ServiceCollectionMocks.cs # services.Mock<T>(...)
    SmtpClientMocks.cs        # intent helpers for one dependency
  Mail/
    GivenMailSender.cs        # namespace mirrors the production namespace + .Tests
```

## Rules

1. **Naming.** Name the class `Given<SubjectUnderTest>` and each test `When<Condition>Then<ExpectedOutcome>`. No `Async`
   suffix and no underscores.
2. **AAA pattern.** Every test has three sections, in that order. Each one opens with a lowercase C# comment
   (`// arrange`, `// act`, `// assert`), and exactly one blank line comes before the next section.
   - Arrange at least resolves the subject.
   - Act is a single statement.
   - Assertions appear only in the assert section.
3. **Layered happy flow.** Each layer states only what differs from the layer above:
   - **Assembly:** `Testing.Configure(...)` registers every dependency in its successful state (mocks that succeed,
     valid options, silent logging). It is the only place with one-time `[Before(Assembly)]` process setup.
   - **Class:** a `private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null)` builds on
     `Configure` and adds the defaults for this subject.
   - **Test:** passes only the deviation for its scenario. A test never rebuilds the whole graph.
4. **One scenario per test.** Several assertions are fine when they describe the same outcome. Variations of the same
   scenario use `[Arguments]`, not copy-pasted tests.
5. **Parallel by default.** Build a fresh provider per test, and give each test unique data (`Guid.NewGuid()`,
   per-test ids). Use a keyed `[NotInParallel("key")]` only for real destructive shared state.
6. **Mocks are abstracted.** Test classes never reference `A.`, `Substitute.`, `Mock<>`, `It.` or `Arg.`. The library
   appears only in `Mocks/`, and our own names always say mock, never fake, stub or substitute, whatever the library
   underneath: the `Mocks/` folder, `<Dependency>Mocks` helper classes, a `Mocked` alias and `mocked` parameter for the
   dependency, and `Mock<T>()` to register one. `Mocked` and `mocked` stay inside `Mocks/`: everywhere else the
   lambda parameter is `x`, as in `services.Mock<ISmtpClient>(x => x.RejectsConnection())`. There are three kinds of helper:
   - registration: `services.Mock<T>(configure)`, which replaces any existing registration with a singleton mock;
   - state, as extension methods named by intent: `client.AcceptsEverything()`, `user.Anonymous()`;
   - verification, as extension methods named by expectation: `client.ShouldHaveAuthenticatedAs(name)`.

   Switching mocking libraries then touches only `Mocks/`.
7. **Mock only boundaries** (I/O, network, time, external services). Use real implementations for your own logic,
   value objects and validators.
8. **Assertions** are AwesomeAssertions: `actual.Should().Be(expected)`, chained with `.And` when they describe the
   same outcome. Await only the async ones (`await act.Should().ThrowAsync<TException>()`). Don't mix in TUnit's
   `Assert.That`.

## Testing.cs

```csharp
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Dotfiles.Mail.Tests;

public sealed class Testing {
    public static DateTimeOffset Now { get; } = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Before(Assembly)]
    public static void UseEnglishMessages() => CultureInfo.DefaultThreadCurrentUICulture = new("en-US");

    /// <summary>Assembly happy flow: every dependency registered in its successful state.</summary>
    public static IServiceProvider Configure(Action<IServiceCollection>? scenario = null) {
        var services = new ServiceCollection()
            .AddLogging()
            .AddMail();                                          // production registration under test
        services.Use<TimeProvider>(new FakeTimeProvider(Now));
        services.Mock<ISmtpClient>(x => x.AcceptsEverything());
        scenario?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static MailMessage AnyMessage() => new($"subject-{Guid.NewGuid():n}", "<p>body</p>");
}

public static class TestingExtensions {
    public static T Resolve<T>(this IServiceProvider provider) where T : notnull => provider.GetRequiredService<T>();

    public static IServiceCollection Use<T>(this IServiceCollection services, T instance) where T : class {
        services.Replace(new ServiceDescriptor(typeof(T), instance));
        return services;
    }
}
```

## Mocks

```csharp
using FakeItEasy;

namespace Dotfiles.Mail.Tests.Mocks;

using Mocked = ISmtpClient;

public static class ServiceCollectionMocks {
    public static IServiceCollection Mock<T>(this IServiceCollection services, Action<T>? configure = null)
        where T : class {
        var mocked = A.Fake<T>();
        configure?.Invoke(mocked);
        return services.Use(mocked);
    }
}

public static class SmtpClientMocks {
    public static Mocked AcceptsEverything(this Mocked mocked) {
        A.CallTo(() => mocked.SendAsync(A<MailMessage>._, A<CancellationToken>._)).Returns(Task.CompletedTask);
        return mocked;
    }

    public static Mocked RejectsConnection(this Mocked mocked) {
        A.CallTo(() => mocked.ConnectAsync(A<string>._, A<int>._, A<CancellationToken>._))
            .ThrowsAsync(new IOException("Connection refused"));
        return mocked;
    }

    public static void ShouldHaveAuthenticatedAs(this Mocked mocked, string username) =>
        A.CallTo(() => mocked.AuthenticateAsync(username, A<string>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

    public static void ShouldNotHaveAuthenticated(this Mocked mocked) =>
        A.CallTo(() => mocked.AuthenticateAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
}
```

The same helpers in the other libraries:

| Helper body   | FakeItEasy                                    | NSubstitute                         | Moq (existing projects)                                |
|---------------|-----------------------------------------------|-------------------------------------|--------------------------------------------------------|
| create        | `A.Fake<T>()`                                 | `Substitute.For<T>()`               | `new Mock<T>()`, register `mock.Object`                |
| stub          | `A.CallTo(() => x.M(A<int>._)).Returns(v)`    | `x.M(Arg.Any<int>()).Returns(v)`    | `mock.Setup(x => x.M(It.IsAny<int>())).Returns(v)`     |
| verify once   | `A.CallTo(() => x.M(1)).MustHaveHappenedOnceExactly()` | `x.Received(1).M(1)`       | `Mock.Get(x).Verify(m => m.M(1), Times.Once)`          |
| verify never  | `.MustNotHaveHappened()`                      | `x.DidNotReceive().M(1)`            | `Times.Never`                                          |

With Moq, the state helpers extend `Mock<T>`, as in the reference repository. Verification helpers still extend the
resolved interface through `Mock.Get(instance)`.

## A test class

```csharp
namespace Dotfiles.Mail.Tests;

using static Testing;

public sealed class GivenMailSender {
    [Test]
    public async Task WhenCredentialIsCompleteThenItAuthenticatesBeforeSending() {
        // arrange
        var provider = Setup();
        var sut = provider.Resolve<IMailSender>();

        // act
        await sut.SendAsync(AnyMessage());

        // assert
        provider.Resolve<ISmtpClient>().ShouldHaveAuthenticatedAs("sender@domain");
    }

    [Test]
    public async Task WhenPasswordIsEmptyThenItSendsAnonymously() {
        // arrange
        var provider = Setup(services => services
            .Use(new OutgoingAccount("sender@domain", string.Empty)));
        var sut = provider.Resolve<IMailSender>();

        // act
        await sut.SendAsync(AnyMessage());

        // assert
        provider.Resolve<ISmtpClient>().ShouldNotHaveAuthenticated();
    }

    [Test]
    public async Task WhenServerRejectsConnectionThenItReturnsConnectionFailed() {
        // arrange
        var provider = Setup(services => services
            .Mock<ISmtpClient>(x => x.RejectsConnection()));
        var sut = provider.Resolve<IMailSender>();

        // act
        var result = await sut.SendAsync(AnyMessage());

        // assert
        result.Error.Should().Be(MailCodes.ConnectionFailed);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(65536)]
    public void WhenPortIsOutOfRangeThenAccountIsInvalid(int port) {
        // arrange
        var account = new OutgoingAccount("sender@domain", "secret") { Port = port };

        // act
        var result = account.Validate();

        // assert
        result.Successful.Should().BeFalse();
    }
}

file static class Helper {
    public static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Configure(services => {
        services.Use(new OutgoingAccount("sender@domain", "secret"));
        scenario?.Invoke(services);
    });
}
```

## Web endpoints

Share one factory per test session, and override only the scenario's mocks per test:

```csharp
public sealed class WebApp : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.Mock<IPaymentGateway>(x => x.Approves()));

    public HttpClient Client(Action<IServiceCollection>? scenario = null) => scenario is null
        ? CreateClient()
        : WithWebHostBuilder(builder => builder.ConfigureTestServices(scenario)).CreateClient();
}

[ClassDataSource<WebApp>(Shared = SharedType.PerTestSession)]
public sealed class GivenCreateOrderEndpoint(WebApp app) {
    [Test]
    public async Task WhenPaymentIsDeclinedThenItReturnsPaymentRequired() {
        // arrange
        using var client = app.Client(services => services
            .Mock<IPaymentGateway>(x => x.Declines()));

        // act
        using var response = await client.PostAsJsonAsync("/orders", AnyOrder());

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }
}
```

## Mediator: validators, request handlers, event handlers

In a mediator application layer (MediatR or compatible, with FluentValidation), each part of a request gets its own
test class in the request's mirrored feature folder, isolated from the other two:

| Part            | Class                    | Subject                                 | Isolation in `Setup`                               |
|-----------------|--------------------------|-----------------------------------------|----------------------------------------------------|
| Validator       | `Given<Request>Validator` | `provider.GetValidator<Request>()`      | none, the real validator with mocked boundaries    |
| Request handler | `Given<Request>Handler`  | `provider.Resolve<IMediator>()`         | `AlwaysValid<Request>()`, `ClearTriggers<Event>()` |
| Event handler   | `Given<EventHandler>`    | `provider.ResolveHandler<SUT, Event>()` | `ClearTriggersExcept<SUT, Event>()`                |

- Alias the types per file (`Request`, `Event`, `SUT`), and build input from a private `ValidRequest()` or
  `ValidEvent()` that each test changes in one place.
- Assert on what crossed a boundary by capturing it (`x.OnSent(emails.Add)`), and on a published event with
  `services.OnTriggered<Event>(...)`.

Read [references/mediator.md](references/mediator.md) before writing any of these tests. It has the helper
implementations and a full example of each part.

## Notes

- Test projects are never published as Native AOT, so proxy-based mocking libraries are fine there. Production code
  must still use AOT-safe patterns.
- When migrating: `[Fact]`/`[Test]` become `[Test]`; `[Theory]` + `[InlineData]` become `[Test]` + `[Arguments]`; NUnit
  `[SetUpFixture]` + `[OneTimeSetUp]` become `[Before(Assembly)]` in `Testing.cs`; fixtures shared through the
  constructor become `ClassDataSource<T>`; `Assert.*` calls become `Should()` assertions. Then wrap the existing mock setups in `Mocks/` helpers.