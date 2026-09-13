---
name: spectre-console
description: "Render rich console output in C# with Spectre.Console: markup colors, tables, trees, panels, status spinners, progress bars, live displays and prompts. Use when writing or reviewing code that calls AnsiConsole, Markup, Table, Status, Progress, Live or prompts, or when user/dynamic text is printed through markup and must be escaped."
license: MIT
---

# Spectre.Console

Upstream docs: https://spectreconsole.net. The LLM index at https://spectreconsole.net/llms.txt links to every how-to,
widget and reference page as Markdown, so fetch the specific page you need.

```csharp
#:package Spectre.Console@0.57.2

using Spectre.Console;
```

This repo uses only `Spectre.Console` for rendering. Command-line parsing is done by ConsoleAppFramework, not
`Spectre.Console.Cli`, which binds settings classes with reflection.

## Markup and escaping

- Markup syntax is `[red]ERROR[/]`, `[bold blue]...[/]`, `[link=https://...]text[/]`. A literal bracket is written `[[`
  or `]]`.
- **Never** put dynamic text (user input, file names, API responses, exception messages) into a markup string
  unescaped. A `[` in the data throws or renders as styling. Use one of these:
  - `AnsiConsole.MarkupLineInterpolated($"[blue]File:[/] {fileName}")`, which escapes interpolated values automatically.
  - `Markup.Escape(value)` or `value.EscapeMarkup()` when building the string yourself.
- `Markup.Remove(styled)` strips markup, e.g. to produce plain text for a log.
- Use `AnsiConsole.Write(new Text(value, style))` for text that should never be parsed as markup.

## Widgets

```csharp
var table = new Table().Border(TableBorder.Simple);
table.AddColumn("Project");
table.AddColumn(new TableColumn("Total").RightAligned());
table.AddRow(Markup.Escape(row.Project), row.Total.ToString());
AnsiConsole.Write(table);
```

Other widgets are `Tree`, `Panel`, `Grid`, `Columns`, `Rows`, `Layout`, `Rule`, `BarChart`, `BreakdownChart` and
`Calendar`. `JsonText` lives in the separate `Spectre.Console.Json` package.

## Status, progress and live displays

```csharp
var result = await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots12)
    .SpinnerStyle(Style.Parse("blue"))
    .StartAsync("Getting information", async ctx => {
        ctx.Status("Getting user information");   // update the text inside the callback
        return await LoadAsync(cancellationToken);
    });
```

- Use `StartAsync` for async work, and keep it paired with `await`.
- Live rendering (Status, Progress, Live) and prompts are **not thread-safe**. Only one live display may run at a time,
  so don't nest them or start prompts inside one.
- Don't write to the console from other threads while a live display is running. Update the display through its context
  (`ctx.Status(...)`, `task.Increment(...)`, `ctx.Refresh()`), and print summaries after `StartAsync` returns.
- For parallel work, run the tasks with `Task.WhenAll` inside one `Progress().StartAsync(...)` with one `ProgressTask`
  per operation.
- Pass the `CancellationToken` to the work. Catch exceptions inside the callback, or let them surface after the display
  ends so the terminal isn't left half-rendered.

## Prompts

`TextPrompt<T>`, `SelectionPrompt<T>`, `MultiSelectionPrompt<T>` and `AnsiConsole.Confirm(...)`. Check
`AnsiConsole.Profile.Capabilities.Interactive` first, and fall back to options or arguments when input is redirected or
running in CI.

## Output routing

`AnsiConsole` writes to stdout. For diagnostics that must not pollute piped stdout, create a console on stderr:
`AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) })`.