# .NET Native AOT binary size

How to measure what a file-based C# app in `csharp/` costs in binary size, which `#:property` switches help, and what
each library adds. The numbers below come from `ClockifyCli.cs` (`dotclock`) on osx-arm64 with SDK 10.0.403; other
targets differ a little, but the proportions hold.

## Measure first

Always compare publish output, not build output, and strip symbols the same way the mise plugin and the workflow do:

```shell
cd csharp
dotnet publish -c Release -p:CopyOutputSymbolsToPublishDirectory=false -o /tmp/size-base ClockifyCli.cs
stat -f %z /tmp/size-base/dotclock        # macOS; use `stat -c %s` on Linux
```

Change one thing, publish into a new folder, compare. The build must stay at zero `IL2xxx`/`IL3xxx` warnings; a
switch that only wins by turning a feature off silently is not a win.

## Switches that paid off

Add these as `#:property` lines after the `AssemblyName`/`Version` block. The table shows the effect of each switch
alone on the 8,488,264 B baseline.

| Property                                                                            | Saving  | Cost                                                                |
|-------------------------------------------------------------------------------------|---------|---------------------------------------------------------------------|
| `StackTraceSupport=false`                                                           | −646 KB | Uncaught exceptions print frame addresses instead of method names   |
| `OptimizationPreference=Size`                                                       | −199 KB | Slightly slower code; irrelevant for a CLI                          |
| `MetricsSupport=false` + `HttpActivityPropagationSupport=false`                     | −83 KB  | No `System.Diagnostics.Metrics`, no W3C trace headers on HttpClient |
| `GenerationMode = JsonSourceGenerationMode.Metadata` on the `JsonSerializerContext` | −17 KB  | No serialization fast path; fine for small bodies                   |

Together: 7,560,136 B, −11%.

Switches that were measured and rejected for a console tool:

| Property                                                                                                                                                                                             | Saving  | Why not                                                                                                                                                                                                                     |
|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `InvariantGlobalization=true`                                                                                                                                                                        | −180 KB | Day and month names switch to English. .NET picks up the macOS `AppleLocale` (`id_ID`) even with `LANG=C.UTF-8`, so `timesheet` output changes. Use it only when the tool prints no culture-aware dates (`WebhookCli` does) |
| `UseSystemResourceKeys=true`                                                                                                                                                                         | −32 KB  | Framework exception messages become resource keys                                                                                                                                                                           |
| `DebuggerSupport=false`, `EventSourceSupport=false`, `StripSymbols=true`, `IlcGenerateStackTraceData=false`, `IlcFoldIdenticalMethodBodies=true`, `JsonSerializerIsReflectionEnabledByDefault=false` | 0       | Already the Native AOT default, or already in effect on this platform                                                                                                                                                       |
| `UseNativeHttpHandler`                                                                                                                                                                               | –       | iOS/Android only                                                                                                                                                                                                            |

## Where the bytes go: cumulative stages

The most useful view is a chain of tiny sample apps, each adding one library on top of the previous one, published
with the same switches as the real tool. The step cost is the price of that library together with everything it
drags in from CoreLib.

| Stage | Added                                                                       | Binary  | Step     |
|-------|-----------------------------------------------------------------------------|---------|----------|
| 0     | Blank `Console.WriteLine` (runtime, GC, minimal CoreLib)                    | 1.04 MB | 1.04 MB  |
| 1     | `HttpClient.GetStringAsync` over HTTPS                                      | 4.47 MB | +3.44 MB |
| 2     | System.Text.Json source generation (record with enum, nested array)         | 4.92 MB | +454 KB  |
| 3     | ConsoleAppFramework (two commands, options, help)                           | 4.93 MB | +16 KB   |
| 4     | NetEscapades.EnumGenerators (`TryParse`, `ToStringFast`, `[Description]`)   | 4.95 MB | +16 KB   |
| 5     | Spectre.Console `Table`, markup, `Color.TryFromHex`                         | 5.98 MB | +1.05 MB |
| 6     | Spectre.Console `Status` spinner and `Live` table                           | 6.12 MB | +146 KB  |
| 7     | `[GeneratedRegex]`                                                          | 6.15 MB | +32 KB   |
| 8     | LINQ (`Select`, `Where`, `GroupBy`, `ToDictionary`, `OrderBy`, `ToHashSet`) | 6.24 MB | +97 KB   |
| –     | Real `dotclock` (more DTOs, more LINQ shapes, more Spectre, own code)       | 7.21 MB | +967 KB  |

What this says:

- HTTPS is the floor. `HttpClient` alone is almost half the binary: `System.Net.Http`, TLS (`System.Net.Security`),
  X.509 and ASN.1, sockets, DNS, `Uri`, plus the async machinery, generic collections and formatting it pulls from
  CoreLib. Every tool that talks to an API pays this once.
- Spectre.Console is the second cost (about 1.2 MB with live and status). Its own assembly is only ~330 KB; the rest
  is CoreLib, LINQ and `System.Collections.Immutable` generics that nothing else needed. PrettyConsole is the slimmer
  choice when a tool only prints colored lines.
- Source generators are nearly free. ConsoleAppFramework and EnumGenerators cost 16 KB each because they ship no
  runtime library.
- LINQ grows with every distinct generic shape (`GroupBy<(string, DateOnly), ...>`), not with the number of calls.

### Reproducing the stages

Each stage is one file-based app in a scratch folder (not in `csharp/`) with the same `#:property` header as the real
tool. Name the files `s<N>_<what>.cs` so they sort in stage order: `s0_blank.cs`, `s1_http.cs`, `s2_json.cs`,
`s3_caf.cs`, `s4_enum.cs`, `s5_spectre_table.cs`, `s6_spectre_live.cs`, `s7_regex.cs`, `s8_linq.cs`. Stage 0 is
`Console.WriteLine("hi")`; stage 1 adds `new HttpClient().GetStringAsync(args[0])`; stage 2 adds a
`JsonSerializerContext` with one record; and so on, each stage keeping the previous code so the step cost is isolated.
From that folder, pass them in order to `shell/bin/analyze-dotnet-binary.sh`, which publishes each one and writes the
size table with the step from the previous stage:

```shell
analyze-dotnet-binary.sh -b none -o stages.md \
  -i s0_blank.cs -i s1_http.cs -i s2_json.cs -i s3_caf.cs -i s4_enum.cs \
  -i s5_spectre_table.cs -i s6_spectre_live.cs -i s7_regex.cs -i s8_linq.cs
```

Keep the sample code representative: a `Table` with markup and `Color.TryFromHex` exercises more of Spectre than a
bare `AnsiConsole.WriteLine`, and a record with an enum and a nested array exercises more of System.Text.Json than a
flat DTO.

## Where the bytes go: per assembly in the real binary

ILCompiler can write a map of every object it emitted. `shell/bin/analyze-dotnet-binary.sh` publishes the app with the
map enabled and sums the bytes by source (.NET libraries, .NET runtime tables, NuGet packages, app code), per NuGet
package and version, per assembly and per node kind. The source comes from each assembly's path in the `.ilc.rsp`
file: the runtime pack (`microsoft.netcore.app.runtime.*`) is .NET, any other `<id>/<version>/lib/` folder is a NuGet
package, and the first input is the app. The report also names the runtime pack, because a newly installed SDK changes
the numbers (SDK 11.0.100 builds `dotclock` against net11.0 at 7,551,896 B):

```shell
analyze-dotnet-binary.sh -i csharp/ClockifyCli.cs -o report.md
analyze-dotnet-binary.sh -i csharp/ClockifyCli.cs -p InvariantGlobalization=true -o invariant.md
```

Other options: `-r linux-x64` for another target, `-t N` for the number of assemblies listed, `-b all` for a breakdown
of every input, and `-k DIR` to keep the publish output, logs and map files. Publish runs in Release with
`CopyOutputSymbolsToPublishDirectory=false`, like the mise plugin, and the report counts trim/AOT warnings per app.

How it works: the script passes `-p:IlcGenerateMapFile=true` and points `NativeIntermediateOutputPath` at its own
folder. Without that, a file-based app writes the map into its build cache
(`~/Library/Application Support/dotnet/runfile/<Name>-<hash>/obj/release/native/` on macOS), not the output folder.
The map is an XML list of `<MethodCode Name="..." Length="..."/>`,
`<ConstructedEEType ...>` and similar nodes. Node names are mangled: assembly name with `.` replaced by `_`, then
namespace and type, so `System_Text_Json_...` belongs to System.Text.Json and `S_P_CoreLib_...` to
System.Private.CoreLib. Prefixes such as `_ZTV<n>`, `__writableData`, `__GCSTATICS`, `Boxed_` and `unbox_` wrap the
same names and must be stripped before matching. The assembly list comes from the `-r:` lines in the sibling
`dotclock.ilc.rsp`.

Result for `dotclock` (mapped total 8.42 MB; the map counts some data before dehydration, so it is larger than the
7.56 MB file):

| Assembly                                                                  | Bytes   | Share |
|---------------------------------------------------------------------------|---------|-------|
| System.Private.CoreLib                                                    | 3.03 MB | 36%   |
| Runtime tables (type system, vtables, dehydrated data, frozen objects)    | 2.09 MB | 25%   |
| System.Text.Json                                                          | 673 KB  | 8%    |
| System.Net.Http                                                           | 460 KB  | 5.5%  |
| Spectre.Console + Spectre.Console.Ansi                                    | 335 KB  | 4%    |
| System.Linq                                                               | 240 KB  | 2.9%  |
| `dotclock` (own code plus generated ConsoleAppFramework, JSON, enum code) | 236 KB  | 2.8%  |
| System.Security.Cryptography + System.Formats.Asn1                        | 226 KB  | 2.7%  |
| System.Net.Security                                                       | 180 KB  | 2.1%  |
| System.Net.Sockets, Primitives, NameResolution, NetworkInformation        | 175 KB  | 2.1%  |
| System.Text.RegularExpressions                                            | 150 KB  | 1.8%  |
| System.Private.TypeLoader                                                 | 139 KB  | 1.6%  |
| Collections.Immutable, Concurrent, Console, DiagnosticSource, Numerics    | 245 KB  | 2.9%  |
| NetEscapades.EnumGenerators.Attributes                                    | 0.8 KB  | 0%    |

By node kind: compiled method code 4.1 MB (49%), type descriptors 1.2 MB, dehydrated runtime data 1.0 MB, frozen
string and object literals 0.5 MB, GC info 0.4 MB, reflection metadata 0.2 MB.

The map file is around 18 MB; the script deletes it unless `-k` is given.

## Checklist for a new tool

1. Start from the header in `Developing-CSharp-Script.md` and add `OptimizationPreference=Size`,
   `StackTraceSupport=false`, `MetricsSupport=false` and `HttpActivityPropagationSupport=false`.
2. Add `InvariantGlobalization=true` only when the tool prints no dates, numbers or names that depend on the user's
   locale.
3. Use `GenerationMode = JsonSourceGenerationMode.Metadata` on the `JsonSerializerContext` unless the tool serializes
   large payloads in a hot loop.
4. Prefer PrettyConsole over Spectre.Console when the tool only needs colored lines; use Spectre only for tables,
   live displays and spinners.
5. Measure before and after with the publish command above, and keep the build at zero trim/AOT warnings.
6. Expect the result to be dominated by HTTPS and the runtime. Own code is a few percent; rewriting it rarely moves
   the number.