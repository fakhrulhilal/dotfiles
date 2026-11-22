# Dotfiles wiki

Usage guides for the personal CLI tools in and a few setup notes. The pages are generated from
[`docs/`](../docs) in the repository, so send changes there, not through the wiki editor.

## Tools

Every tool is also a C# file-based app, so with the .NET 10 SDK (10.0.300 or later) it runs straight from a clone, 
from inside `csharp/`. We can install globally (see [README.md](../README.md#tools) for installation details), or execute 
it directly

```shell
# option 1: when installed globally
dotkafka init --partition 5

# option 2: direct execution, requires .NET 10 SDK (10.0.300 or later)
dotnet run KafkaCli.cs -- init --partition 5
./csharp/KafkaCli.cs init --partition 5
```

`HashGenerator` and `OidcClient` have no release, so run them this way, so use direct execution as option 2.

### Conventions shared by the tools

- **Settings order:** command line option first, then an environment variable. Each page lists the variable names.
- Each CLI should respect common variable used for defining resource, such as PostgreSQL uses `PGUSER`, `PGHOST`, etc 
  for defining connection. This is another environment variable fallback
- **Connection URLs:** connections to servers use a URL-like format,
  `[scheme://][user:password@]host[:port][/path][?option=value]`. A `secure=true` query option, or a scheme such as
  `https`, `ssl` or `tls`, `secure`, turns on TLS. When `trustServerCertificate` defined in query string, then every 
  helper should ignore security certificate from the server. Other query options configure the client. 
  See [../csharp/models/Url.cs](`Url.cs`) for more details.
- **Exit codes:** `0` on success, `1` when validation or the request fails.
- **Help:** every command accepts `-h` / `--help`.

## Guides

- [Developing C# scripts](Developing-CSharp-Script.md)
- [Connecting to Tailscale](Connecting-to-Tailscale.md)
- [Connecting to git repositories on the same server through SSH](Connecting-to-Git-SSH-to-Same-Server.md)
