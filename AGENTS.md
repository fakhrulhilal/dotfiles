# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, GitHub Copilot, Gemini CLI, OpenCode, ...) working in this
repository.

## What this repo is

Personal dotfiles for macOS/Linux (plus a Windows PowerShell profile), and a small set of personal CLI tools written as
C# file-based apps (`csharp/`) and Bun/TypeScript scripts (`js/`). There is no test suite, and there is no build system
for the repo as a whole.

## Agent skills

Skills live in `config/ai/skills/` and are tracked in git. They are wired up in these places:

- `.claude/skills` is a tracked symlink to `config/ai/skills/`.
- `.agents/skills` is a symlink that `shell/configure/ai.sh` creates. It is git-ignored, because `config/git-ignore.txt`
  ignores `.agents`.
- `shell/bootstrap.sh` also links `~/.claude/skills` and `~/.agents/skills`, so every project on the machine gets these
  skills.

`config/ai/skills/SOURCES.md` records where each skill came from. Vendored skills are verbatim upstream copies pinned to a
commit; refresh them from upstream instead of editing. Skills marked `local` are edited here. **This file wins over
generic advice in any skill.**

| Area                                               | Skill                                                         |
|----------------------------------------------------|---------------------------------------------------------------|
| C# file-based apps, `#:` directives                | `csharp-scripts`                                              |
| Native AOT / trimming warnings                     | `dotnet-aot-compat`                                           |
| Modern C# language features, async                 | `modern-csharp`, `csharp-async`                               |
| Minimal APIs (`WebhookCli`, `OidcClient`)          | `minimal-apis` (`dotnet-webapi` only for existing controller projects) |
| Generic Host, DI, options, background work         | `microsoft-extensions`, `worker-services`                     |
| Tests (TUnit, Microsoft.Testing.Platform, fakes)   | `dotnet-testing`, `tunit`, `run-tests`, `filter-syntax`, `platform-detection`, `migrate-vstest-to-mtp` |
| OpenTelemetry                                      | `configuring-opentelemetry-dotnet`                            |
| CLI commands and parsing                           | `consoleappframework`                                         |
| Console output                                     | `spectre-console`, `pretty-console-expert`                    |
| Enums                                              | `netescapades-enumgenerators`                                 |
| Kafka and Schema Registry (`KafkaCli`)             | `kafka-schema-registry`                                       |

## Coding style: follow `.editorconfig`

Check the nearest `.editorconfig` before editing. They live at the repo root and in `csharp/`, `js/`, `powershell/`. Match the formatting of the surrounding file, and don't reformat lines you didn't
change.

| Files                      | Indent  | Max line | Final newline               | Notes                                                                                                      |
|----------------------------|---------|----------|-----------------------------|------------------------------------------------------------------------------------------------------------|
| everything (root defaults) | 4 sp    | 120      | no                          | UTF-8, LF                                                                                                  |
| `*.sh`, `*.bash`, `*.zsh`  | 2 sp    | 120      | no                          | `shell/` must stay POSIX (no zsh- or bash-only syntax)                                                     |
| `*.xml,html,json,yml,yaml` | 2 sp    | 120      | no                          | YAML: no value alignment, `{ a }` / `[ a ]` with inner spaces                                              |
| `*.sql`                    | 4 sp    | 120      | **yes**                     | trim trailing whitespace                                                                                   |
| `*.md`                     | 4 sp    | 120      | no                          | wrap long text at 120; one space after `#`, `-`, `>`; one blank line around headers and blocks; aligned tables |
| `*.cs`                     | 4 sp    | 120      | **yes**                     | see below                                                                                                  |
| `*.ts`, `*.js`             | 4 sp    | 120      | no                          | double quotes, semicolons, `{` on same line, `} else {` / `} catch {` on one line, sorted import members   |
| `*.ps1`, `*.psd1`, `*.psm1`| 4 sp    | 115      | no; **yes** in PorkBunClient| braces on the next line (Allman); `else`, `catch`, `finally` on a new line                                 |

C# (`csharp/.editorconfig`):

- K&R braces: `{` stays on the same line, but `else`, `catch` and `finally` start a new line (`}` then `else {`).
- File-scoped namespaces. `using` directives go outside the namespace, `System.*` first, with no blank lines between
  groups. Unused usings (IDE0005) and unused parameters are warnings.
- Use `var` everywhere, language keywords (`string`, not `String`), and no `this.` qualifier.
- Braces are optional for single-line bodies (`if (x) return y;`) and required once a body spans multiple lines.
- Use expression bodies for methods, local functions and operators only when they fit on one line.
- Prefer pattern matching, `is not`, switch expressions, index/range, throw expressions, the `default` literal, `using var`
  and static local functions.
- Add clarifying parentheses in arithmetic, relational and other binary expressions.
- When wrapping, put the operator at the start of the line. Chop long object/collection initializers onto one item per
  line.
- Attributes go on their own line.
- Never more than one blank line. Leave a blank line after a block and after `return`, `break`, `continue` or `throw`
  before the next statement.
- PascalCase for types, methods, properties, events and non-private constants. Interfaces start with `I`.
- Modifier order: `public private protected internal file static extern new virtual abstract sealed override readonly unsafe volatile async`.

## .NET standards (every .NET project)

These apply to every .NET project, including the tools in `csharp/`. For work under `csharp/`, delegate to the
`csharp-engineer` subagent. It is defined in `csharp/.claude/agents/csharp-engineer.md` and linked from `.claude/agents/`.

### Tests (`dotnet-testing`)

- New test projects use TUnit on Microsoft.Testing.Platform: `OutputType=Exe`, only the `TUnit` package, and `global.json`
  with `"test": { "runner": "Microsoft.Testing.Platform" }`. Never add `Microsoft.NET.Test.Sdk` or `coverlet.*`.
  Existing xUnit/NUnit/MSTest projects stay as they are unless a migration is requested, and then move to TUnit.
- Name test classes `Given<Subject>` and tests `When<Condition>Then<Outcome>`, with one scenario per test.
- Every test has arrange, act and assert blocks, in that order, separated by one blank line.
- The happy flow is layered:
  - `Testing.cs` configures the whole assembly in its successful state and holds the readability helpers
    (`Resolve<T>()`, `Use(instance)`, shared test data).
  - Each class adds its own defaults in a private `Setup(...)`.
  - Each test passes only what differs for its scenario.
- Tests never call the mocking library directly. Fakes are registered, configured and verified through intent-named
  helpers in `Fakes/` (`client.RejectsConnection()`, `client.ShouldHaveAuthenticatedAs(name)`).
  - Use FakeItEasy.
  - Use NSubstitute for a new project where FakeItEasy doesn't fit.
  - Use Moq only in projects that already use it.
- Keep TUnit's parallel default: build a fresh service provider per test and give each test unique data.

### HTTP APIs (`minimal-apis`)

- New APIs use Minimal APIs, never controllers. Existing controller projects keep controllers.
- Each endpoint is one `IEndpoint` class in `<Feature>/Endpoints/<Action>.cs`, with its own nested `Request`/`Response`
  and a private static `Handle`. `Endpoints.cs` lists every endpoint explicitly in feature and access groups; there is no
  reflection-based scanning.
- Expose filters as `RouteHandlerBuilder` extensions. Respond with `TypedResults`. Validate with DataAnnotations and
  `builder.Services.AddValidation()`.

### Startup work (`worker-services`)

- Heavy startup work (database migration, seeding, cache warm-up, loading remote metadata) runs in its own
  `BackgroundService`, registered with `AddHostedService<T>()`. Never run it in `Program.cs` before `app.Run()` or in
  `IHostedService.StartAsync`; both hold up host startup.
- Resolve scoped services through `IServiceScopeFactory.CreateAsyncScope()`, pass `stoppingToken` everywhere, and expose
  completion through a readiness health check when requests depend on it. `DbMigration` in `csharp/WebhookCli.cs` is
  the in-repo example.

## Dotfiles bootstrap

- Entry point: `zsh shell/bootstrap.sh` (macOS) or `bash shell/bootstrap.sh` (Linux). It sets `DOT_HOME` to the repo
  root, then sources `shell/install/*.sh` and `shell/configure/*.sh` in order. Set `SKIP_INSTALL_MAC_APPS` to skip GUI apps
  (listed in `mac_app.txt`).
- The repo is wired into `$HOME` with **symlinks**, not copies. `relink` (in `shell/functions.sh`) backs up an existing
  target to `*.bak` and then symlinks it. Editing a file in the repo changes the live config right away. Examples:
  `zsh/rc.zsh` → `~/.zshrc`, `bash/rc.sh` → `~/.bashrc`, `config/mise.toml` → `~/.config/mise/config.toml`. Git is
  handled differently: `config/git.txt` is added to `~/.gitconfig` with `[include]`.
- `DOT_HOME`, `ZSH_EXT` and `BASH_EXT` are written into `~/.zshenv` / `~/.env`, and every rc file depends on them.
- How the shell layers load:
  - `shell/*.sh` holds POSIX code shared by zsh and bash (variables, functions, aliases).
  - `zsh/*.zsh` and `bash/*.sh` hold shell-specific code.
  - `shell/rc.sh` runs last and activates mise, oh-my-posh and fnox.
- Secrets never go in the repo. They belong in `~/.zshenv` / `~/.env` or in fnox (`~/.secret`, which is its own git repo).
- Almost all dev tools come from mise (`config/mise.toml`), not Homebrew/apt. To add a tool, add an entry there.

## C# file-based apps (`csharp/`)

These are .NET 10 single-file apps with no `.csproj`. Project settings live in `#:` directives, and `#:include` needs SDK
10.0.300 or later. `docs/Developing-CSharp-Script.md` is the full guide.

```shell
# run from csharp/
dotnet build ClockifyCli.cs                   # AoT/trim analyzers run here; keep IL2xxx/IL3xxx warnings at zero
dotnet run ClockifyCli.cs -- <args>           # or ./ClockifyCli.cs <args>
dotnet clean ClockifyCli.cs
dotnet publish -c Release -p:CopyOutputSymbolsToPublishDirectory=false -o out ClockifyCli.cs
docker build --build-arg SCRIPT_FILE=WebhookCli.cs .    # alpine.Dockerfile for alpine
mise install -f csharp:ClockifyCli            # rebuild an installed mise tool after changes
```

### Layout

- An entry script (`<Name>Cli.cs`) starts with directives, then usings, then top-level statements, then its types.
  Script-local helpers go in `file static class Helper`, imported with `using static Helper`. Script types are `internal`
  and `sealed` unless designed for inheritance.
- Shared code is pulled in with `#:include`, not project references:
  - `helpers/` (namespace `Dotfiles.Helpers`) has HTTP, Kafka, Postgres, SQL Server, SQLite and Elasticsearch helpers.
  - `models/` (namespace `Dotfiles.Models`) has `Result<TError>`, `Url`, `Duration`, DB config and JSON converters.
    `models/clockify/` holds the Clockify DTOs (`Dotfiles.Models.Clockify`).
  - `web/` (namespace `Dotfiles.Web`) has `WebApp` (a slim host) and `DbHealthCheck`.
- Shared files are `public`, declare a file-scoped namespace, and declare their own dependencies. For example,
  `HttpHelper.cs` includes `../models/Url.cs`, and `web/WebApp.cs` declares `#:sdk Microsoft.NET.Sdk.Web`. Include paths
  are relative to the file that contains the directive. Duplicate `#:package`/`#:property` directives across included
  files break the build.
- Any connection to certain server (f.e. Redis, DB, etc) should be compatible with URL like format, 
  handled in `models/Url.cs`
- Try to avoid exception by leveraging Result pattern from `models/Result.cs`
- Header template. The shebang ends with `--` so script args pass through. Put a blank line between directive groups, and
  `chmod +x` the file:

  ```csharp
  #!/usr/bin/env -S dotnet --

  #:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
  #:property AssemblyName=clock#
  #:property EnumGenerator_EnumMetadataSource=DescriptionAttribute

  #:include ./helpers/HttpHelper.cs
  #:include ./models/Result.cs

  #:package ConsoleAppFramework@5.7.13
  #:package NetEscapades.EnumGenerators@1.0.0-beta21*
  #:package Spectre.Console@0.57.2
  ```

- Pin at least the major version of every package (`@5.7.13`, `@10`, `@1.0.0-beta21*`). Don't add new `@*` references.
- Use modern C# already in use here: primary constructors, collection expressions, `required` members, records for DTOs,
  C# 14 `extension(HttpClient client) { ... }` blocks (`HttpHelper`), and the `field` keyword (`Result`).
- Return errors as values with `Result<TError>`. Return an error-code enum or a value and let the implicit conversions
  do the rest. Check results with `if (result is not Result<ValidationCodes>.Success<T> success)` and print
  `result.Error.ToStringFast(true)`. Throw exceptions only for programmer errors or unusable configuration. Commands
  return `int` exit codes.
- Settings come from the CLI option first, then an environment variable (e.g. `CLOCKIFY_API_KEY`). Document the fallback
  in the XML `<param>` comment. Connection settings are URLs parsed by `models/Url.cs`, where query extras such as
  `trustServerCertificate=true` configure the client. `build_url` in `shell/functions.sh` builds these URLs from
  `<NAME>_HOST`, `<NAME>_PORT` and related variables.
- Build lookup tables once as `FrozenDictionary`/`FrozenSet`, using `Const.CompareMode`
  (`StringComparer.InvariantCultureIgnoreCase`).
- Console output: use Spectre.Console for tables, spinners and markup (escape dynamic text) and PrettyConsole
  (`Console.WriteLineInterpolated($"{CC.Red}Error{CC.Default}: ...")`) for lightweight colored lines.

### Native AOT (required)

File-based apps publish as Native AOT by default. Never set `PublishAot=false`. Code must build with zero trim/AOT
warnings.

- **JSON: System.Text.Json source generation only.**
  - Declare `internal sealed partial class JsonOpt : JsonSerializerContext` with `[JsonSerializable(typeof(T))]` for
    every root type. Use arrays as `T[]` and `IAsyncEnumerable<T>` for streamed responses.
  - Add `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]`.
  - Pass `JsonOpt.Default.<Type>` to every `JsonSerializer.Serialize`/`Deserialize` call. Shared helpers take a
    `JsonTypeInfo<T>` parameter (`HttpHelper.Get`, `PostgreHelper`, `SqliteHelper`) instead of a reflection-based
    generic `T`.
  - Use one context per naming policy (`DbOpts` snake_case for storage, `WebOpts` camelCase for HTTP). Contexts owned by a
    helper get a prefix (`ElasticJsonOpt`, `SqlServerJsonOpt`).
  - Use `DbOpts` as a manual free mapping and compatible with AoT, no need ORM at all.
  - For minimal APIs, register the context with `options.SerializerOptions.TypeInfoResolverChain.Insert(0, WebOpts.Default)`.
  - In new scripts, add `#:property JsonSerializerIsReflectionEnabledByDefault=false` so a missing type fails fast.
  - Rename a record property with `[property: JsonPropertyName("lang")]`.
- **Configuration:**
  - Add `#:property EnableConfigurationBindingGenerator=true`.
  - Register with `builder.Services.AddOptionsWithValidateOnStart<TConfig, TValidator>().BindConfiguration("Section")`.
  - Validators are DataAnnotations plus `[OptionsValidator] public sealed partial class Validator : IValidateOptions<T>`
    (source-generated), or a hand-written `IValidateOptions<T>`.
- **Logging:** `[LoggerMessage]` static partial extension methods in `internal static partial class LoggerExtensions`. No
  interpolated strings in log calls.
- **Regex:** `[GeneratedRegex]` partial members only.
- **Web host:** `WebApp.CreateBuilder(args)` (slim builder; environment variables and args as the only config sources;
  JSON console logging outside Development). For custom parameter binding, use `IBindableFromHttpContext<T>`.
- **CLI:** ConsoleAppFramework, which is source-generated. Don't declare command parameters of complex object types or
  JSON arrays, because they bind through reflection-based JSON. Take a file path and deserialize it with a `JsonTypeInfo`.
- **Forbidden in script code:** `Activator`, `Type.GetType(string)`, `MakeGenericType`, reflection-based `System.Enum`
  APIs, `dynamic`, Newtonsoft.Json, and the non-generic `JsonStringEnumConverter`. Never silence IL warnings with
  `#pragma` or `[UnconditionalSuppressMessage]`.
- **A dependency that isn't trim-safe** (Confluent.Kafka, Avro): add `#:property TrimMode=partial` and
  `#:property TrimmerRootDescriptor=<Script>TrimmerRoots.xml`. That XML must be named after the script and root the
  offending assemblies with `preserve="all"` (see `KafkaCliTrimmerRoots.xml`).
- **No debug symbols in shipped output:** web scripts set `#:property StripSymbols=true`, the mise plugin publishes with
  `CopyOutputSymbolsToPublishDirectory=false`, and the Dockerfile deletes `*.dbg`.

### Enums

- Any enum that is parsed from text, printed, or used as an error code gets `[EnumExtensions]`
  (NetEscapades.EnumGenerators). Use the generated `ToStringFast()`, `TryParse`, `IsDefined`, `GetValues` and
  `GetNames`. Never use `ToString()` or `Enum.Parse`/`TryParse`/`GetValues`/`GetNames` on these enums.
- Display text and alternate tokens go in `[Description("...")]`, with
  `#:property EnumGenerator_EnumMetadataSource=DescriptionAttribute`. Render with `value.ToStringFast(true)` and parse
  with `MyEnum.TryParse(text, out var value, ignoreCase: true, allowMatchingMetadataAttribute: true)`. See
  `TimesheetPeriod`, where the CLI token `this-week` maps to `ThisWeek`.
- Error-code enums are named `<Area>Codes` (`ValidationCodes`, `GetInfoCodes`). The first member is `None`, and every
  other member has a `[Description]` holding the user-facing message. They serve as `TError` in `Result<TError>`.
- The generated `TryParse` also accepts numeric strings, so check `IsDefined(value)` when the input is untrusted.
- Give explicit numeric values to members that are persisted or sent over the wire (`Sqlite = 1, Postgre = 2`).
- JSON:
  - Serialize enums as strings via `UseStringEnumConverter = true` on the context.
  - When a member's wire name differs from the naming policy, mark it `[JsonStringEnumMemberName("DOES_NOT_CONTAIN")]`.
  - For a framework enum inside a DTO, use `[property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))]`.
- Validate enum options with `[EnumDataType(typeof(T), ErrorMessage = "...")]` or the generated `IsDefined`.
- Separate attributed members with one blank line. An enum with no attributes may sit on one line:
  `enum FormatType { Json, Curl, Fetch, Netcat }`.

### Installing scripts as mise tools

`csharp/mise-plugin/` is a vfox backend plugin. `config/mise.toml` registers it as `vfox-backend:csharp`. A tool entry
`"csharp:<Name>" = "latest"` runs `dotnet publish -c Release` on `csharp/<Name>.cs` into the mise install dir. Every
script reports the single version `latest`, so a change only takes effect after `mise install -f csharp:<Name>`.

Prebuilt releases: `.github/workflows/csharp.yml` builds every `csharp:<Name>` entry for linux-x64, win-x64, win-x86
and osx-arm64 (Native AOT has no linux-x86) with `csharp/package.sh`, checks it with `csharp/smoke.sh` and uploads
`<AssemblyName>-<target>.tar.gz/.zip` as workflow artifacts. Each script carries its own `#:property Version=`.
To release one tool, bump that `Version` and push to master. Every master run releases each script whose version has
no tag `<AssemblyName>-v<Version>` yet (e.g. `dotclock-v0.1.0`) and creates that tag. Never push these tags by hand.
Users install it through the `[tool_alias]` entries in `config/mise.toml` (`mise use -g dotclock`). Add an alias
for each new tool: mise caches versions per tool name, so two `github:fakhrulhilal/dotfiles[...]` specs would share
one cache.

`WebhookCli.cs` is deployed with `csharp/docker-compose.yaml`, which adds an ngrok sidecar. The matching Bruno/OpenCollection
requests are in `endpoints/webhook/`.

## JS/TS scripts (`js/`)

These are Bun scripts (`#!/usr/bin/env -S bun run`). Run `bun install` in `js/` before using them. `.npmrc` sends
`@jsr` packages to JSR. CLIs use `@cliffy/command`, and `libraries/` holds shared clients (HTTP, JSON-RPC). `otlp.ts`
sets up OpenTelemetry tracing (`traceInvocation`). The loose `.js` files (Telerik helpers, `clockify.js`) are
browser-console snippets, not Node modules.

## Other directories

- `services/*/docker-compose.yaml`: local infrastructure stacks. Each stack has an `env.txt` template and uses OrbStack
  `dev.orbstack.domains` labels.
- `powershell/`: `_profile.ps1` dot-sources every `*.ps1` in that folder. `Modules/PorkBunClient` is a standard module
  with `Public/`, `Private/` and a PSScriptAnalyzer settings file.
- `neovim/` holds the current nvim config (lazy.nvim). `vim/` is the legacy setup; `vim/basic-rc.txt` is symlinked to
  `~/.vimrc` and `~/.ideavimrc`.

## Conventions

- Commit messages use conventional-commit style with a scope, e.g. `feat(tool): ...`, `fix(script): ...`,
  `feat(config): ...`.