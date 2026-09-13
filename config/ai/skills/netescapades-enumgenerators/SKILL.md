---
name: netescapades-enumgenerators
description: "Use NetEscapades.EnumGenerators source-generated enum helpers (ToStringFast, TryParse, Parse, IsDefined, GetValues, GetNames, HasFlagFast) instead of reflection-based System.Enum APIs in Native AOT C# code. Use when declaring enums that are parsed from strings, rendered as text, carry human-readable messages via [Description]/[Display]/[EnumMember], or when replacing Enum.ToString, Enum.Parse, Enum.TryParse, Enum.IsDefined, Enum.GetValues or Enum.GetNames calls."
license: MIT
---

# NetEscapades.EnumGenerators

A source generator that emits a `<EnumName>Extensions` class full of `switch`-based helpers. There is no reflection, so
it is safe for Native AOT and trimming, and much faster than the `System.Enum` APIs.

Upstream docs: https://github.com/andrewlock/NetEscapades.EnumGenerators/blob/main/docs/README.md

## Setup

File-based app:

```csharp
#:property EnumGenerator_EnumMetadataSource=DescriptionAttribute
#:package NetEscapades.EnumGenerators@1.0.0-beta21*

using System.ComponentModel;
using NetEscapades.EnumGenerators;
```

Project file: `<PackageReference Include="NetEscapades.EnumGenerators" Version="1.0.0-beta21" />`. Do **not** set
`PrivateAssets="all"`: the metapackage has runtime dependencies (`EnumParseOptions`, `SerializationOptions`).

| MSBuild property                     | Values                                                                              | Effect                                                                                                            |
|--------------------------------------|-------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| `EnumGenerator_EnumMetadataSource`   | `EnumMemberAttribute` (default), `DisplayAttribute`, `DescriptionAttribute`, `None` | Which attribute the `useMetadataAttributes` / `allowMatchingMetadataAttribute` overloads read                     |
| `EnumGenerator_ForceInternal`        | `true` / `false`                                                                    | Makes every generated extension class `internal`                                                                  |
| `EnumGenerator_EnableUsageAnalyzers` | `true`                                                                              | Warns (NEEG004-NEEG012) wherever `System.Enum` APIs are used instead of the generated ones                        |

Settings on the attribute itself:

- `[EnumExtensions(MetadataSource = MetadataSource.DisplayAttribute)]`
- `ExtensionClassName`, `ExtensionClassNamespace`
- `IsInternal = true`
- `[EnumExtensions<ExternalEnum>]` for enums you don't own, e.g. `DayOfWeek`

## Generated API

| Use                                                                       | Instead of                                                        |
|---------------------------------------------------------------------------|-------------------------------------------------------------------|
| `value.ToStringFast()`                                                    | `value.ToString()`                                                |
| `value.ToStringFast(useMetadataAttributes: true)`                         | reading `[Description]`/`[Display]`/`[EnumMember]` via reflection |
| `MyEnum.TryParse(name, out var value, ignoreCase, allowMatchingMetadata)` | `Enum.TryParse`                                                   |
| `MyEnum.Parse(name, ignoreCase, allowMatchingMetadata)`                   | `Enum.Parse` (throws `ArgumentException`)                         |
| `MyEnum.IsDefined(value)` / `MyEnum.IsDefined(name, allowMatchingMetadata)` | `Enum.IsDefined`                                                  |
| `MyEnum.GetValues()` / `MyEnum.GetNames()` / `MyEnumExtensions.Length`    | `Enum.GetValues` / `Enum.GetNames`                                |
| `value.HasFlagFast(flag)` (only on `[Flags]` enums)                       | `value.HasFlag(flag)`                                             |

The static helpers are generated on `MyEnumExtensions`. With C# 14 (.NET 10 SDK) they are also exposed as static extension
members, so you can call them on the enum type itself (`MyEnum.TryParse(...)`). `MyEnumExtensions.TryParse(...)` also
works.

## Patterns

Human-readable error codes. Set `EnumGenerator_EnumMetadataSource=DescriptionAttribute`, reserve `None` for 0, and render
the message with `ToStringFast(true)`:

```csharp
[EnumExtensions]
enum ValidationCodes {
    None,

    [Description("File path is not set")]
    FileUnset,

    [Description("Clockify API key is not set")]
    ApiKeyUnset
}

AnsiConsole.MarkupLine($"[red]ERROR[/]: {result.Error.ToStringFast(true)}");
```

CLI tokens that differ from member names. Put the token in the metadata attribute and parse with metadata matching turned
on:

```csharp
[EnumExtensions]
enum TimesheetPeriod {
    [Description("this-week")]
    ThisWeek,

    [Description("last-week")]
    LastWeek
}

if (!TimesheetPeriod.TryParse(period.Replace('_', '-'), out var parsed, ignoreCase: true,
        allowMatchingMetadataAttribute: true))
    return ValidationCodes.InvalidPeriod;
```

Small enums with no metadata fit on one line: `[EnumExtensions] enum FormatType { Json, Curl, Fetch, Netcat }`.

## Pitfalls

- `TryParse` also accepts numeric strings: `"42"` succeeds and yields `(MyEnum)42`, even when 42 is not a defined member.
  Follow it with `IsDefined(value)` when the input is untrusted.
- Metadata attributes are ignored unless they match the configured metadata source. For example, `[Description]` does
  nothing while the source is still the default `EnumMemberAttribute`.
- The generator does not change JSON. With System.Text.Json, use `[JsonStringEnumMemberName("...")]` on members and
  `UseStringEnumConverter = true` on the `JsonSerializerContext`. For a single property of an external enum, use
  `[property: JsonConverter(typeof(JsonStringEnumConverter<T>))]`. Always use the generic converter; the non-generic
  `JsonStringEnumConverter` is not AOT-safe.
- ConsoleAppFramework binds `enum` parameters with `Enum.TryParse(ignoreCase: true)`, which ignores metadata. To accept
  metadata tokens such as `this-week`, take a `string` parameter and parse it with the generated `TryParse`.