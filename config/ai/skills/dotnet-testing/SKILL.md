---
name: dotnet-testing
description: "House style for .NET tests: TUnit on Microsoft.Testing.Platform, Given/When/Then naming, strict arrange-act-assert, layered happy-flow setup (assembly Testing.cs, then class Setup, then per-test scenario), and fakes hidden behind intent-named helpers (FakeItEasy preferred, NSubstitute for new projects otherwise, Moq only where it is already used). Use when creating a test project, writing or reviewing tests, adding fakes or mocks, or migrating tests from xUnit, NUnit or MSTest."
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
| Fakes      | FakeItEasy. NSubstitute for a new project where FakeItEasy doesn't fit. Moq only in a project that already uses it     |
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
    <PackageReference Include="TUnit" Version="1.67.0" />
    <PackageReference Include="FakeItEasy" Version="9.0.1" />
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
  Fakes/                      # the only folder that references the mocking library
    ServiceCollectionFakes.cs # services.Fake<T>(...)
    SmtpClientFakes.cs        # intent helpers for one dependency
  Mail/
    GivenMailSender.cs        # namespace mirrors the production namespace + .Tests
```

## Rules

1. **Naming.** Name the class `Given<SubjectUnderTest>` and each test `When<Condition>Then<ExpectedOutcome>`. No `Async`
   suffix and no underscores.
2. **Arrange, act, assert.** Every test has three blocks, in that order, separated by exactly one blank line, with no
   `// Arrange` comments.
   - Arrange at least resolves the subject.
   - Act is a single statement.
   - Assertions appear only in the assert block.
3. **Layered happy flow.** Each layer states only what differs from the layer above:
   - **Assembly:** `Testing.Configure(...)` registers every dependency in its successful state (fakes that succeed,
     valid options, silent logging). It is the only place with one-time `[Before(Assembly)]` process setup.
   - **Class:** a `private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null)` builds on
     `Configure` and adds the defaults for this subject.
   - **Test:** passes only the deviation for its scenario. A test never rebuilds the whole graph.
4. **One scenario per test.** Several assertions are fine when they describe the same outcome. Variations of the same
   scenario use `[Arguments]`, not copy-pasted tests.
5. **Parallel by default.** Build a fresh provider per test, and give each test unique data (`Guid.NewGuid()`,
   per-test ids). Use a keyed `[NotInParallel("key")]` only for real destructive shared state.
6. **Fakes are abstracted.** Test classes never reference `A.`, `Substitute.`, `Mock<>`, `It.` or `Arg.`. The library
   appears only in `Fakes/`, behind three kinds of helper:
   - registration: `services.Fake<T>(configure)`, which replaces any existing registration with a singleton fake;
   - state, as extension methods named by intent: `client.AcceptsEverything()`, `user.Anonymous()`;
   - verification, as extension methods named by expectation: `client.ShouldHaveAuthenticatedAs(name)`.

   Switching mocking libraries then touches only `Fakes/`.
7. **Fake only boundaries** (I/O, network, time, external services). Use real implementations for your own logic,
   value objects and validators.
8. **Assertions** are TUnit's async assertions and are always awaited:
   `await Assert.That(actual).IsEqualTo(expected)`.

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
        services.Fake<ISmtpClient>(client => client.AcceptsEverything());
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

## Fakes

```csharp
using FakeItEasy;

namespace Dotfiles.Mail.Tests.Fakes;

public static class ServiceCollectionFakes {
    public static IServiceCollection Fake<T>(this IServiceCollection services, Action<T>? configure = null)
        where T : class {
        var fake = A.Fake<T>();
        configure?.Invoke(fake);
        return services.Use(fake);
    }
}

public static class SmtpClientFakes {
    public static ISmtpClient AcceptsEverything(this ISmtpClient client) {
        A.CallTo(() => client.SendAsync(A<MailMessage>._, A<CancellationToken>._)).Returns(Task.CompletedTask);
        return client;
    }

    public static ISmtpClient RejectsConnection(this ISmtpClient client) {
        A.CallTo(() => client.ConnectAsync(A<string>._, A<int>._, A<CancellationToken>._))
            .ThrowsAsync(new IOException("Connection refused"));
        return client;
    }

    public static void ShouldHaveAuthenticatedAs(this ISmtpClient client, string username) =>
        A.CallTo(() => client.AuthenticateAsync(username, A<string>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

    public static void ShouldNotHaveAuthenticated(this ISmtpClient client) =>
        A.CallTo(() => client.AuthenticateAsync(A<string>._, A<string>._, A<CancellationToken>._))
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
    private static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Configure(services => {
        services.Use(new OutgoingAccount("sender@domain", "secret"));
        scenario?.Invoke(services);
    });

    [Test]
    public async Task WhenCredentialIsCompleteThenItAuthenticatesBeforeSending() {
        var provider = Setup();
        var sut = provider.Resolve<IMailSender>();

        await sut.SendAsync(AnyMessage());

        provider.Resolve<ISmtpClient>().ShouldHaveAuthenticatedAs("sender@domain");
    }

    [Test]
    public async Task WhenPasswordIsEmptyThenItSendsAnonymously() {
        var provider = Setup(services => services.Use(new OutgoingAccount("sender@domain", string.Empty)));
        var sut = provider.Resolve<IMailSender>();

        await sut.SendAsync(AnyMessage());

        provider.Resolve<ISmtpClient>().ShouldNotHaveAuthenticated();
    }

    [Test]
    public async Task WhenServerRejectsConnectionThenItReturnsConnectionFailed() {
        var sut = Setup(services => services.Fake<ISmtpClient>(client => client.RejectsConnection()))
            .Resolve<IMailSender>();

        var result = await sut.SendAsync(AnyMessage());

        await Assert.That(result.Error).IsEqualTo(MailCodes.ConnectionFailed);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(65536)]
    public async Task WhenPortIsOutOfRangeThenAccountIsInvalid(int port) {
        var account = new OutgoingAccount("sender@domain", "secret") { Port = port };

        var result = account.Validate();

        await Assert.That(result.Successful).IsFalse();
    }
}
```

## Web endpoints

Share one factory per test session, and override only the scenario's fakes per test:

```csharp
public sealed class WebApp : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.Fake<IPaymentGateway>(gateway => gateway.Approves()));

    public HttpClient Client(Action<IServiceCollection>? scenario = null) => scenario is null
        ? CreateClient()
        : WithWebHostBuilder(builder => builder.ConfigureTestServices(scenario)).CreateClient();
}

[ClassDataSource<WebApp>(Shared = SharedType.PerTestSession)]
public sealed class GivenCreateOrderEndpoint(WebApp app) {
    [Test]
    public async Task WhenPaymentIsDeclinedThenItReturnsPaymentRequired() {
        using var client = app.Client(services => services.Fake<IPaymentGateway>(gateway => gateway.Declines()));

        using var response = await client.PostAsJsonAsync("/orders", AnyOrder());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PaymentRequired);
    }
}
```

## Notes

- Test projects are never published as Native AOT, so proxy-based mocking libraries are fine there. Production code
  must still use AOT-safe patterns.
- When migrating: `[Fact]`/`[Test]` become `[Test]`; `[Theory]` + `[InlineData]` become `[Test]` + `[Arguments]`; NUnit
  `[SetUpFixture]` + `[OneTimeSetUp]` become `[Before(Assembly)]` in `Testing.cs`; fixtures shared through the
  constructor become `ClassDataSource<T>`. Then wrap the existing mock setups in `Fakes/` helpers.