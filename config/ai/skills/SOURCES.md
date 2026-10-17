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
| `dotnet-testing`                   | local  | [cleanarchitecture-kit tests](https://github.com/fakhrulhilal/cleanarchitecture-kit/tree/master/tests), [TUnit](https://tunit.dev) | - | TUnit 1.73.19, AwesomeAssertions 9.6.0, FakeItEasy 9.0.1 |
| `dotnet-clean-architecture`        | local  | [martinothamar/Mediator](https://github.com/martinothamar/Mediator) README, [ServiceScan.SourceGenerator](https://github.com/Dreamescaper/ServiceScan.SourceGenerator) README, layering and pipeline from a private MediatR application layer | - | Mediator 3.0.2, FluentValidation 12.2.0, ServiceScan.SourceGenerator 3.2.2, .NET 10 |
| `topcoat`                          | local  | [tokio-rs/topcoat](https://github.com/tokio-rs/topcoat) `llms.txt`, `docs/`, `demos/coffee-shop`                               | -  | 0.10.0, `341f3ff2fe16` |
| `leptos`                           | local  | [leptos-rs/book](https://github.com/leptos-rs/book) `src/` (`c573962`), [rust-ui/leptos-ui](https://github.com/rust-ui/leptos-ui) registry, `leptos_ui` and `ui-cli` (`9cb8e9f`), [leptos-fetch](https://github.com/zakstucke/leptos-fetch) README (`1c904ac`), [start-axum](https://github.com/leptos-rs/start-axum) template, todo-app `frontend/leptos` (CSR, Trunk, Tailwind, Rust/UI) | - | leptos 0.8.22, leptos_ui 0.3.22, tw_merge 0.1.22, leptos-fetch 0.4.10, Tailwind 4.3.3, reactive_stores 0.4.4 |
| `rust-backend`                     | local  | todo-dx `api/` (local project, axum JSON API), [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457), ASP.NET Core [health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) and [options pattern](https://learn.microsoft.com/dotnet/core/extensions/options) | - | axum 0.8.9, utoipa 6.0.0, sqlx 0.9.0, turso 0.8.2, figment 0.10.19 |
| `docker-container`                 | local  | this repo's `csharp/Dockerfile`, `services/*.compose.yaml` and AGENTS.md compose conventions, `rust-binary-project` Dockerfile, [tiny-hc](https://github.com/fakhrulhilal/tiny-hc) | - | tiny-hc 0.1.3, .NET 10 |
| `rust-ntex`                        | local  | [docs.ntex.rs](https://docs.ntex.rs) guide (`ntex/docs/` at `7c3b758`), [ntex.rs](https://ntex.rs/docs) docs ([ntex-rs/website](https://github.com/ntex-rs/website) `a4f26cf`), [ntex-rs/examples](https://github.com/ntex-rs/examples) (`0fd78f7`); template verified with compio on macOS | - | ntex 4.3.0, ntex-service 5.2.0, compio 0.19.2 (compio-runtime 0.12.6), Rust 1.99 |

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