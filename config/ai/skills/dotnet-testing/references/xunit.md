# Staying on xUnit

Use this when an existing xUnit project keeps xUnit for now. Everything in `SKILL.md` still applies (naming, AAA,
layered happy flow, mocks behind `Mocks/`, AwesomeAssertions). Only the framework mechanics below change.

## Mapping

| TUnit (SKILL.md)                       | xUnit v2                                                        | xUnit v3                                   |
|----------------------------------------|-----------------------------------------------------------------|--------------------------------------------|
| `[Test]`                               | `[Fact]`                                                        | `[Fact]`                                   |
| `[Test]` + `[Arguments(...)]`          | `[Theory]` + `[InlineData(...)]`                                | same                                       |
| `[MethodDataSource]`                   | `[MemberData(nameof(...))]` returning `TheoryData<...>`         | same                                       |
| `[Before(Assembly)]` in `Testing.cs`   | `[ModuleInitializer]` static method in `Testing.cs`             | `[ModuleInitializer]`, or an assembly fixture for async/disposable setup |
| `ClassDataSource<T>(Shared = PerTestSession)` | `IClassFixture<T>` (per class) or `[Collection]` + `ICollectionFixture<T>` | `[assembly: AssemblyFixture(typeof(T))]` |
| `[NotInParallel("key")]`               | put the classes in one `[Collection("key")]`                    | same                                       |
| `TestContext.Current.CancellationToken`| `CancellationToken.None`                                        | `TestContext.Current.CancellationToken`    |

## Project

Keep xUnit, swap the rest of the stack: `FluentAssertions` becomes `AwesomeAssertions`, and both namespaces go global so
test files only import what is specific to them.

```xml
<ItemGroup>
  <PackageReference Include="AwesomeAssertions" Version="9.6.0" />
</ItemGroup>

<ItemGroup>
  <Using Include="AwesomeAssertions" />
  <Using Include="Xunit" />
</ItemGroup>
```

- v2 runs on VSTest (`Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`); leave that as is unless a runner
  migration is requested (`migrate-vstest-to-mtp`).
- v3 (`xunit.v3`) runs on MTP natively: `<OutputType>Exe</OutputType>` and `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`.

## Things that differ from TUnit

- **Test methods and their parameter types must be public.** A `[Theory]` parameter can't be a `file` or `internal`
  type, so a scenario enum used in `[InlineData]` is a `public enum` next to the class. The class `Setup` and the
  readability helpers still live in `file static class Helper`, imported with `using static Helper;`.
- **Prefer an enum over raw values in `[InlineData]`** when the input is computed (tokens, payloads, dates):
  `[InlineData(Credential.Expired)]` reads in the test explorer, a raw JWT doesn't. The helper maps the enum to the
  real value.
- **Parallelism is per class.** Test classes run in parallel; tests in one class run one after another. A fresh
  provider per test still matters because a `[Collection]` or fixture can widen what is shared.
- **No class-level static setup attribute.** Don't use the constructor or `IDisposable` as hidden setup. The test
  calls `Setup(...)` in arrange, the same as in TUnit.
- **Don't inject `ITestOutputHelper` unless a test logs through it.** Logging in the happy flow is silent
  (`services.AddLogging()` with no providers).

## Middleware example

A middleware is resolved from the provider like any subject. `Setup` registers it with a recording `RequestDelegate`
so a test can tell whether the request went through, and the request gets a scope as its `RequestServices`, the way
ASP.NET Core does it:

```csharp
namespace Sharp.Tests.Middleware;

using static Helper;
using SUT = Sharp.AuthenticationMiddleware.AuthenticationMiddleware;

public sealed class GivenAuthenticationMiddleware {
    [Theory]
    [InlineData(Credential.Missing)]
    [InlineData(Credential.Malformed)]
    [InlineData(Credential.Expired)]
    public async Task WhenCredentialIsNotValidThenRequestIsRejected(Credential credential) {
        // arrange
        var provider = Setup();
        var sut = provider.Resolve<SUT>();
        var context = provider.RequestWith(credential);

        // act
        await sut.InvokeAsync(context);

        // assert
        context.User.Identity!.IsAuthenticated.Should().BeFalse();
        context.ReachedNextMiddleware().Should().BeFalse();
    }

    [Fact]
    public async Task WhenIdentityProviderIsUnreachableThenRequestIsRejected() {
        // arrange
        var provider = Setup(services => services
            .Mock<ISharpIdentityClient>(x => x.IsUnreachable()));
        var sut = provider.Resolve<SUT>();
        var context = provider.RequestWith(Credential.Valid);

        // act
        await sut.InvokeAsync(context);

        // assert
        context.User.Identity!.IsAuthenticated.Should().BeFalse();
    }
}

public enum Credential { Missing, Malformed, Valid, Expired }

file static class Helper {
    private const string NextMiddleware = nameof(NextMiddleware);

    public static IServiceProvider Setup(Action<IServiceCollection>? scenario = null) => Testing.Configure(services => {
        services.AddSingleton<RequestDelegate>(_ => context => {
            context.Items[NextMiddleware] = true;
            return Task.CompletedTask;
        });
        services.AddTransient<SUT>();
        scenario?.Invoke(services);
    });

    public static HttpContext RequestWith(this IServiceProvider provider, Credential credential) {
        var context = new DefaultHttpContext {
            RequestServices = provider.CreateScope().ServiceProvider,
            Response = { Body = new MemoryStream() }
        };
        if (credential != Credential.Missing) context.Request.Headers.Authorization = AuthorizationHeader(credential);
        return context;
    }

    // per-request services come from the request scope, as UseMiddleware does
    public static Task InvokeAsync(this SUT sut, HttpContext context) =>
        sut.InvokeAsync(context, context.RequestServices.Resolve<ISharpAuthenticationService>());

    public static bool ReachedNextMiddleware(this HttpContext context) => context.Items.ContainsKey(NextMiddleware);
}
```

When the subject reads the clock through a library that can't take a `TimeProvider` (for example
`JwtSecurityTokenHandler` lifetime checks), build the input relative to `DateTime.UtcNow` with margins of hours, not
seconds, so the test can't flake.
