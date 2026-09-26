# Dotfiles wiki

Usage guides for the personal CLI tools in and a few setup notes. The pages are generated from
[`docs/`](../docs) in the repository, so send changes there, not through the wiki editor.

## Tools

| Tool                              | Binary     | What it does                                                    |
|-----------------------------------|------------|-----------------------------------------------------------------|
| [ClockifyCli](ClockifyCli.md)     | `dotclock` | Clockify: bulk-add time entries, timesheet per working day      |
| [KafkaCli](KafkaCli.md)           | `dotkafka` | Kafka: manage topics and Avro schemas, produce messages         |
| [WebhookCli](WebhookCli.md)       | `dothook`  | Webhook receiver that stores requests, verifies HMAC signatures |

### Getting a tool

These tools are released as Native AOT binaries for Linux x64, Windows x64/x86 and macOS
arm64. Install one with [mise](https://mise.jdx.dev) by adding an alias to `~/.config/mise/config.toml`:

```toml
[tool_alias]
dotclock = "github:fakhrulhilal/dotfiles[version_prefix=dotclock-v]"
dotkafka = "github:fakhrulhilal/dotfiles[version_prefix=dotkafka-v]"
dothook = "github:fakhrulhilal/dotfiles[version_prefix=dothook-v]"
```

```shell
# option 1: use github release
mise use -g dotkafka

# option 2: clone repo and direct installation
mise use -g csharp:KafkaCli
```

The releases are listed on the [releases page](https://github.com/fakhrulhilal/dotfiles/releases). Every tool is also a
C# file-based app, so with the .NET 10 SDK (10.0.300 or later) it runs straight from a clone, from inside `csharp/`:

```shell
# option 1: when installed through mise
dotkafka init --partition 5

# option 2: direct execution, requires .NET 10 SDK (10.0.300 or later)
dotnet run KafkaCli.cs -- init --partition 5
./csharp/KafkaCli.cs init --partition 5
```

`HashGenerator` and `OidcClient` have no release, so run them this way, so use direct execution as option 2.

### Conventions shared by the tools

- **Settings order:** command line option first, then an environment variable. Each page lists the variable names.
- **Connection URLs:** connections to servers use a URL-like format,
  `[scheme://][user:password@]host[:port][/path][?option=value]`. A `secure=true` query option, or a scheme such as
  `https`, `ssl` or `tls`, turns on TLS. Other query options configure the client. 
  See [../csharp/models/Url.cs](`Url.cs`) for more details.
- **Exit codes:** `0` on success, `1` when validation or the request fails.
- **Help:** every command accepts `-h` / `--help`.

## Guides

- [Developing C# scripts](Developing-CSharp-Script.md)
- [Connecting to Tailscale](Connecting-to-Tailscale.md)
- [Connecting to git repositories on the same server through SSH](Connecting-to-Git-SSH-to-Same-Server.md)