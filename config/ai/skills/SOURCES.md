# Skill sources

Vendored skills are verbatim copies of the upstream folder at the pinned commit. Don't edit them. To refresh one, copy
the folder again from a newer commit and update its row. Skills marked `local` were written for this repo, and `fork`
means an upstream skill that was changed here. Edit both kinds in place.

| Skill                              | Kind   | Source                                                                    | Upstream path                                                       | Commit / basis |
|------------------------------------|--------|---------------------------------------------------------------------------|---------------------------------------------------------------------|----------------|
| `csharp-scripts`                   | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-advanced/skills/csharp-scripts`                     | `4ecd7d9c76fa` |
| `dotnet-aot-compat`                | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-upgrade/skills/dotnet-aot-compat`                   | `4ecd7d9c76fa` |
| `dotnet-webapi`                    | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-aspnetcore/skills/dotnet-webapi`                    | `4ecd7d9c76fa` |
| `configuring-opentelemetry-dotnet` | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-aspnetcore/skills/configuring-opentelemetry-dotnet` | `4ecd7d9c76fa` |
| `run-tests`                        | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-test/skills/run-tests`                              | `4ecd7d9c76fa` |
| `platform-detection`               | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-test/skills/platform-detection`                     | `4ecd7d9c76fa` |
| `filter-syntax`                    | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-test/skills/filter-syntax`                          | `4ecd7d9c76fa` |
| `migrate-vstest-to-mtp`            | vendor | [dotnet/skills](https://github.com/dotnet/skills)                         | `plugins/dotnet-test-migration/skills/migrate-vstest-to-mtp`        | `4ecd7d9c76fa` |
| `csharp-async`                     | vendor | [github/awesome-copilot](https://github.com/github/awesome-copilot)       | `skills/csharp-async`                                               | `7568a482ce2d` |
| `modern-csharp`                    | vendor | [managedcode/dotnet-skills](https://github.com/managedcode/dotnet-skills) | `catalog/Tools/Modern-CSharp/skills/modern-csharp`                  | `d26ba3c9610b` |
| `tunit`                            | vendor | [managedcode/dotnet-skills](https://github.com/managedcode/dotnet-skills) | `catalog/Testing/TUnit/skills/tunit`                                | `d26ba3c9610b` |
| `worker-services`                  | vendor | [managedcode/dotnet-skills](https://github.com/managedcode/dotnet-skills) | `catalog/Frameworks/Worker-Services/skills/worker-services`         | `d26ba3c9610b` |
| `microsoft-extensions`             | vendor | [managedcode/dotnet-skills](https://github.com/managedcode/dotnet-skills) | `catalog/Libraries/Microsoft-Extensions/skills/microsoft-extensions` | `d26ba3c9610b` |
| `minimal-apis`                     | fork   | [managedcode/dotnet-skills](https://github.com/managedcode/dotnet-skills) | `catalog/Frameworks/Minimal-APIs/skills/minimal-apis`               | `d26ba3c9610b` + [StructuredMinimalApi](https://github.com/fakhrulhilal/StructuredMinimalApi) endpoint layout, BackgroundService startup, TUnit tests |
| `kafka-schema-registry`            | vendor | [confluentinc/agent-skills](https://github.com/confluentinc/agent-skills) | `skills/kafka-schema-registry`                                      | `914d95eff7ff` |
| `pretty-console-expert`            | vendor | [dusrdev/PrettyConsole](https://github.com/dusrdev/PrettyConsole)         | `.agents/skills/pretty-console-expert` (branch `stable`)            | `9a18aba86278` |
| `consoleappframework`              | local  | [Cysharp/ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) README                                           | -  | v5.7.13        |
| `netescapades-enumgenerators`      | local  | [NetEscapades.EnumGenerators docs](https://github.com/andrewlock/NetEscapades.EnumGenerators/blob/main/docs/README.md)         | -  | 1.0.0-beta21   |
| `spectre-console`                  | local  | [spectreconsole.net](https://spectreconsole.net/llms.txt)                                                                      | -  | 0.57.2         |
| `dotnet-testing`                   | local  | [cleanarchitecture-kit tests](https://github.com/fakhrulhilal/cleanarchitecture-kit/tree/master/tests), [TUnit](https://tunit.dev) | - | TUnit 1.67.0, FakeItEasy 9.0.1 |

Considered but not vendored:

- awesome-copilot `editorconfig`: generates new `.editorconfig` files and would overwrite this repo's.
- awesome-copilot `containerize-aspnetcore`: its generic Dockerfile conflicts with `csharp/Dockerfile` (AoT
  `runtime-deps` chiseled image).
- awesome-copilot `dotnet-best-practices`: generic solution-level advice.
- awesome-copilot `csharp-tunit`: says tests in a class run sequentially, which contradicts TUnit's parallel-by-default
  model; `tunit` covers the same ground.
- awesome-copilot `csharp-xunit` / `csharp-nunit` / `csharp-mstest`, and managedcode `xunit` / `nunit` / `mstest` / `web-api`:
  superseded by the TUnit and Minimal API standards.
- dotnet/skills `writing-mstest-tests`, `assertion-quality`, `test-anti-patterns`: MSTest-centric.
- No upstream skill exists yet for FakeItEasy, Npgsql, Microsoft.Data.Sqlite or Microsoft.Data.SqlClient.