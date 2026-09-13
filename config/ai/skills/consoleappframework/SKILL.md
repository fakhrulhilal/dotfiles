---
name: consoleappframework
description: "Build Native AOT friendly C# command-line tools with Cysharp ConsoleAppFramework v5 (source-generated, zero reflection). Use when adding or changing CLI commands, options, aliases, help text, argument parsing, exit codes, cancellation (Ctrl+C), validation, filters/middleware or global options in code that references ConsoleAppFramework ([RegisterCommands], [Command], ConsoleApp.Create/Run)."
license: MIT
---

# ConsoleAppFramework v5

ConsoleAppFramework is a source generator. It emits the whole `ConsoleApp` class (parsing, routing and help), so there
is no runtime dependency and no reflection, and it is AOT-safe by design. It needs C# 13+.

Upstream docs: https://github.com/Cysharp/ConsoleAppFramework

```csharp
#:package ConsoleAppFramework@5.7.13

using ConsoleAppFramework;

ConsoleApp.Create().Run(args);
return 0;
```

## Registering commands

Prefer class-based commands, because XML doc comments only work on methods, not on lambdas or local functions:

```csharp
[RegisterCommands]              // auto-added by ConsoleApp.Create(); [RegisterCommands("prefix")] nests them
internal sealed class Commands {
    /// <summary>
    ///     Bulk add time entries from a JSON file.
    /// </summary>
    /// <param name="path">Path to JSON file.</param>
    /// <param name="apiKey">-k|--key, API key. Fallback to env API_KEY.</param>
    [Command("bulk-add|ba")]    // name|alias; default name is the method name in lower-kebab-case
    public async Task<int> BulkAdd(
        [Argument] string path,                     // positional, no --name
        [HideDefaultValue] string? apiKey = null,   // optional; default hidden from help
        CancellationToken cancellationToken = default) { ... }
}
```

- The `<summary>` becomes the command description. In `<param>`, aliases go before the first comma (`-m|--msg, Text.`).
- `ConsoleApp.Create()` plus `app.Add("name", lambda)` or `app.Add<T>()` also work. `""` registers the root command, and
  a name with spaces (`"foo bar"`) registers a nested command.
- `[Hidden]` hides a command or parameter from help. `ConsoleApp.Version = "..."` controls what `--version` prints.
- `[assembly: ConsoleAppFrameworkGeneratorOptions(DisableNamingConversion = true)]` turns off the kebab-case naming.

## Binding rules

- Option names are lower-kebab-case (`jsonValue` becomes `--json-value`) and match case-insensitively.
- Primitive types use `TryParse`, and `ISpanParsable<T>` types (`DateTime`, `DateOnly`, `Guid`, ...) use their own
  parsers. `bool` is a flag and is always optional.
- `enum` uses `Enum.TryParse(ignoreCase: true)`. It does not read metadata attributes; see the
  `netescapades-enumgenerators` skill for metadata tokens.
- A nullable type or a default value makes an option optional. `params T[]` takes all remaining values.
- A `T[]` accepts comma-separated values, or JSON when the value starts with `[`.
- Any other type is bound as JSON with `JsonSerializer.Deserialize<T>`. **Under Native AOT that fails at runtime** unless
  `ConsoleApp.JsonSerializerOptions` is set to options backed by a source-generated `JsonSerializerContext`. Prefer
  primitive parameters, or a file path you deserialize yourself with a `JsonTypeInfo<T>`.
- `CancellationToken`, `ConsoleAppContext` and `[FromServices]` parameters are never bound from args.
- For custom parsing, implement an attribute with `IArgumentParser<T>` (`static bool TryParse(ReadOnlySpan<char>, out T)`).
- Arguments after `--` are not parsed and are available in `ConsoleAppContext.EscapedArguments`.

## Exit codes, errors and cancellation

- A command that returns `int` or `Task<int>` sets `Environment.ExitCode`. A lambda must declare the return type
  explicitly (`int () => ...`).
- An unhandled exception sets exit code 1 and writes the exception via `ConsoleApp.LogError`. `ValidationException` and
  argument parse failures print only their message.
- Validation attributes from `System.ComponentModel.DataAnnotations` (`[Range]`, `[EmailAddress]`, ...) run after
  binding and before the command executes.
- Adding a `CancellationToken` parameter hooks SIGINT/SIGTERM for a graceful shutdown, which then force-exits after
  `ConsoleApp.Timeout` (5 s by default). Pass the token to every async call.

## Filters and global options

```csharp
internal sealed class ExitCodeFilter(ConsoleAppFilter next) : ConsoleAppFilter(next) {
    public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken) {
        try {
            await Next.InvokeAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            Environment.ExitCode = 2;
            ConsoleApp.LogError(ex.Message);
        }
    }
}
```

- Register a filter globally with `app.UseFilter<T>()`, or per class or method with `[ConsoleAppFilter<T>]`. They run in
  the order global → class → method. Pass state along with `context with { State = ... }`.
- `app.ConfigureGlobalOptions((ref builder) => new GlobalOptions(builder.AddGlobalOption<bool>("-v|--verbose")))`.
  Global options only support constant-able types (primitives, `string`, `enum` and their nullables). Read them from
  `ConsoleAppContext.GlobalOptions`.
- `app.ConfigureServices(...)` adds DI. Commands then receive services through constructor injection or
  `[FromServices]` parameters.