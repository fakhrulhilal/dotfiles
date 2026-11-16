---
name: csharp-notebook
description: "Write, edit, run and check reactive C# notebooks with Reverb (muneeb-devp/reverb), where a notebook is a plain .cs file split into `// %%` cells that also runs with `dotnet run`. Covers the file layout (shebang, `#:package Reverb.Runtime`, `// reverb-notebook v1` header, setup / code / [markdown] / types cells, ids and attributes), output (`Dump`, `Md`, `Html`, `Display`, custom formatters, images), widgets (`Ui.Slider`, `Dropdown`, `Text`, `TextArea`, `Checkbox`, `Number`, `Button`), charts (`Chart.Line`, `Bar`, `Scatter`, `VegaLite`), NuGet packages, the RVB diagnostics and how to fix them, and the `reverb` CLI (new, edit, run, exec, check, export, convert). Use when the user asks for a C# notebook, a data exploration, report or interactive calculator in C#, wants to convert a Jupyter `.ipynb` or Polyglot `.dib` notebook to C#, or edits a `.cs` file that has `// %%` markers, `// reverb-notebook` or `#:package Reverb.Runtime`."
license: MIT
---

# Reactive C# notebooks with Reverb

A Reverb notebook is an ordinary C# file-based app (.NET 10+). `// %%` lines split it into cells. In the Reverb editor,
changing a cell re-runs only the cells that read what it declares. Outside the editor, `dotnet run nb.cs` runs it top to
bottom as a console program: widgets keep their default values and every output prints as text. There is no JSON and
no saved output in the file, so it diffs like code.

Upstream: https://github.com/muneeb-devp/reverb (`README.md`, `samples/`). Tool `Reverb.Notebooks`, library
`Reverb.Runtime`, both 0.1.1.

## File layout

```csharp
#!/usr/bin/env dotnet
#:package Reverb.Runtime@0.1.1
#:package Humanizer@2.14.1
// reverb-notebook v1

// %% setup
using Reverb;
using static Reverb.Notebook;

// %% [markdown] id=intro
// # Sales explorer
// Drag the slider to change the minimum order size.

// %% id=data
var orders = Enumerable.Range(1, 500)
    .Select(i => new Order(i, i % 2 == 0 ? "West" : "East", i * 1.5))
    .ToList();

// %% id=threshold
var threshold = Ui.Slider("Minimum order", min: 0, max: 750, value: 400);

// %% id=summary name="Orders above threshold" hide_code
orders.Where(o => o.Total >= threshold.Value)
      .GroupBy(o => o.Region)
      .Select(g => new RegionCount(g.Key, g.Count()))
      .Dump("Orders above threshold");

// %% types
record Order(int Id, string Region, double Total);
record RegionCount(string Region, int Count);
```

Preamble, in this order: the shebang, the `#:package` lines (Reverb.Runtime plus any NuGet packages), then the header
`// reverb-notebook v1`. Append flags to the header line, such as `// reverb-notebook v1 lazy` (an edit marks the cells
that depend on it as stale instead of re-running them).

| Marker                         | Cell                                                                                 |
|--------------------------------|--------------------------------------------------------------------------------------|
| `// %% setup`                  | Every `using` directive. There is only one, and it comes before every code cell      |
| `// %% id=name`                | Code: top-level statements. `name` is how the editor and diagnostics refer to it     |
| `// %% [markdown] id=name`     | Markdown written as `//` comment lines (`[md]` also works)                           |
| `// %% types`                  | Records, classes, enums, static helper classes. There is only one, and it is the last cell |

- A marker starts at column 0 with `// %%`, then whitespace or the end of the line.
- Ids match `^[a-z0-9_-]{1,32}$`, are unique, and `setup` or `types` can't be used as ids. Pick short names that say what
  the cell holds (`data`, `filters`, `by-region`). Reverb gives a random 6-character id to any cell that has none the
  first time it opens the file, and adds a missing header too.
- Code cell attributes go after the id: `name="Label with spaces"` (shown in the editor) and `hide_code` (shows the
  output only). Quote values that have spaces, and escape `\"` and `\\` inside them.
- The whole file compiles as one program, so cells run in file order: declare a variable above the cells that read it.

## Rules that keep reactivity correct

Reverb works out what each cell reads and writes from Roslyn's semantic model. Both the editor and `reverb check` report
these:

| Id     | Rule                                                                | Fix                                                                 |
|--------|---------------------------------------------------------------------|---------------------------------------------------------------------|
| RVB001 | Only the cell that declares a variable can assign it                | Declare a new variable: `var count2 = count + 1;`                   |
| RVB002 | Anonymous types can't be used across cells                          | Use a record (in the types cell) or a named tuple                   |
| RVB003 | No top-level `return`, and no `goto` into another cell              | Use if/else to skip the rest of a cell, or `throw` to stop it       |
| RVB004 | `using` only in the setup cell, type declarations only in types     | Move the line                                                       |
| RVB005 | Changing another cell's object (`list.Add`, setting a property) won't re-run its readers (warning) | Build new values with LINQ, records and `with` |
| RVB006 | A local function can't be used above the cell that declares it      | Move the cell up, or turn it into a static method in the types cell |
| RVB007 | Ref structs and pointers can't be passed between cells              | Keep that code in one cell                                          |
| RVB105 | More than one setup or types cell, or setup after a code cell       | Merge them, and put setup first                                     |

What this means when you write cells:

- Treat each cell's top-level variables as immutable from the outside. Mutating inside the cell that declared the
  variable is fine (filling a `List<T>` in a loop in the same cell, as `samples/loan-calculator.cs` does).
- An anonymous type is fine when it is created and used in the same cell, for example a projection that ends in
  `.Dump()`. If you assign it to a variable that another cell reads, use a record instead.
- Reverb can't see state kept in static fields or singletons, and it survives between runs. Keep data in top-level
  variables, and restart the kernel when something looks out of date.
- Keep slow work (I/O, HTTP, big loads) in its own cell, upstream of the cells that depend on widgets, so that moving a
  slider doesn't reload the data.

## Output

`using Reverb;` gives you `Dump`, `Ui` and `Chart`. `using static Reverb.Notebook;` gives you `Md`, `Html` and `Display`.

```csharp
orders.Dump();                      // collections become tables, objects become property grids
orders.Dump("Big orders");          // with a title; returns its argument, so it can end a chain
Md($"**{orders.Count}** orders");   // Markdown
Html("<em>hi</em>");                // raw HTML
Display(value);                     // the same as Dump, as a statement
Console.WriteLine("plain text");    // captured as a text stream
File.ReadAllBytes("chart.png").Dump(); // a byte[] holding PNG or JPEG data shows as an image
```

- The rich table shows up to 1,000 rows. The text table (under `dotnet run`, `reverb exec`) shows up to 100 rows,
  columns up to 40 characters wide, and nesting up to 3 levels deep.
- Under `dotnet run`, a title prints as `== Title ==`, then the value as aligned text.
- Custom display for your own types (the formatter registered last wins):

```csharp
Formatters.Register<Money>(m => Output.FromHtml($"<b>{m.Amount:C}</b>", text: $"{m.Amount:C}"));
```

  `Output.FromText`, `Output.FromMarkdown` and `Output.FromHtml(html, text)` build outputs. Always give HTML a `text`
  fallback, because that is what the console and `reverb exec` print.

## Widgets

A widget is a plain variable. Any cell that reads its `.Value` re-runs when the user changes it. There are no callbacks.

| Factory                                                              | `.Value`                                   |
|----------------------------------------------------------------------|--------------------------------------------|
| `Ui.Slider("Size", min: 0, max: 100, value: 50, step: 5)`            | `double`, clamped to min..max              |
| `Ui.Dropdown("Region", ["West", "East"], value: "East")`             | the selected item, of any `T` (defaults to the first) |
| `Ui.Text("Name", value: "Ada")`                                      | `string` (single line)                     |
| `Ui.TextArea("Ids", value: "", rows: 6)`                             | `string` with `\n` line endings; `.Lines` gives its trimmed, non-blank lines |
| `Ui.Checkbox("Show totals", value: true)`                            | `bool`                                     |
| `Ui.Number("Years", 30, min: 1, max: 40, step: 1)`                   | the `INumber<T>` type you passed (`int`, `decimal`...) |
| `Ui.Button("Refresh")`                                               | `int`: how many times it has been clicked  |

- `Ui.TextArea` is not in the published 0.1.1 packages. It comes from the local build in `~/Projects/reverb`, packed
  as `0.1.2-local.*` into `~/.nuget/local-feeds/reverb` (the `reverb-local` source in `~/.nuget/NuGet/NuGet.Config`
  maps only `Reverb.Runtime` and `Reverb.Notebooks` to it). A notebook that uses it needs
  `#:package Reverb.Runtime@0.1.2-local.1` (or newer) and the matching `reverb` tool. Otherwise use `Ui.Text`, and
  accept commas or spaces as separators, because a single-line input drops pasted line breaks.
- In the editor, Enter in a text area adds a new line. The value is sent after a 400 ms pause, or straight away with
  Ctrl/Cmd+Enter.
- Create widgets in their own cell (`id=inputs`) and read `.Value` in the cells below. A cell that reads a button's
  `.Value` runs again on every click, which is handy for re-fetching data.
- The factories throw `ArgumentException` when min > max, when step <= 0, for an empty dropdown, or when the dropdown's
  value isn't one of its options.
- Pick defaults that make sense on their own, because `dotnet run`, `reverb exec` and `reverb export` always use them.

## Charts

```csharp
Chart.Line(sales, x: s => s.Date, y: s => s.Revenue);
Chart.Bar(byProduct, x: p => p.Product, y: p => p.Units, title: "Units by product");
Chart.Scatter(points, x: p => p.Height, y: p => p.Weight, series: p => p.Species);
Chart.Line(rows, x: r => r.Month, y: r => r.Total, xTitle: "Month", yTitle: "Revenue (EUR)");
Chart.VegaLite(json);               // any Vega-Lite v5 spec (a JSON object)
```

- A chart shows where it is created. You don't need `.Dump()`.
- Axis titles default to the lambda member (`s => s.Revenue` becomes `Revenue`). For computed values, pass
  `xTitle:` and `yTitle:`. `series:` splits the data into colored series, and bars with a series sit side by side.
- `DateTime`, `DateTimeOffset` and `DateOnly` x values make a time axis. Numbers make a quantitative axis on line and
  scatter charts, and ordinal bars on a bar chart. Bars keep the data's order, so sort before you chart.
- Only the first 5,000 rows are plotted, so aggregate first. A line chart marks each point when it has 60 points or
  fewer.
- Under `dotnet run`, a chart prints a one-line summary: `[bar chart "Units": 3 points; Product A to C; Units 1 to 9]`.

## Data, arguments, cancellation

```csharp
var lines = File.ReadAllLines(Path.Combine(Notebook.Folder, "data.csv")); // next to the notebook, not the cwd
var input = args.Length > 0 ? args[0] : "default.csv";                    // reverb exec nb.cs -- other.csv
await Task.Delay(5000, Notebook.CancellationToken);                       // stops when the user presses Interrupt
using var http = new HttpClient();
var json = await http.GetStringAsync(url, Notebook.CancellationToken);
```

Top-level `await` works in any cell. Pass `Notebook.CancellationToken` to anything long-running.

## Packages

Add `#:package Id@Version` lines to the preamble, next to the Reverb.Runtime line. Always pin a version. The first run
with a new set of packages builds a kernel (a few seconds), and Reverb caches it in `~/.reverb/kernels`. Reverb reads
`nuget.config`. Put the package's `using` lines in the setup cell.

## The `reverb` CLI

Install it with `dotnet tool install -g Reverb.Notebooks` (needs the .NET 10 SDK).

| Command                                                    | What it does                                                      |
|------------------------------------------------------------|-------------------------------------------------------------------|
| `reverb new nb.cs [--no-edit]`                             | Create a starter notebook and open it                             |
| `reverb edit nb.cs [--port n] [--no-browser]`              | Open the browser editor on 127.0.0.1 with a token                 |
| `reverb run nb.cs [--host addr]`                           | Serve it as an app: outputs and widgets, code hidden              |
| `reverb exec nb.cs [--json] [-- args]`                     | Run it headless and print the outputs. Exits 1 if a cell fails    |
| `reverb check nb.cs`                                       | Compiler and RVB diagnostics. Exits 1 on errors                   |
| `reverb export nb.cs --html [-o out.html] [--no-code]`     | Run it and write one self-contained HTML page that works offline  |
| `reverb convert nb.ipynb\|nb.dib [-o out.cs] [--force]`    | Convert a Jupyter or Polyglot notebook                            |

- `exec --json` prints `{ notebook, ok, cells: [{ id, status, message?, outputs: [{ stream, text } | { title?, data:
  { mime: value } }], diagnostics: [{ id, severity, message, line, column }] }], notebookDiagnostics }`. Statuses
  `error`, `upstream-error` and `stale` count as failures.
- `convert` turns `#r "nuget: X, 1.2"` into `#:package`, moves usings into setup and types into the types cell, and
  prints a warning, with the source cell, for anything it couldn't convert. Run `reverb check` on the result and fix
  the RVB findings (usually RVB001 reassignments and RVB002 anonymous types shared between cells).
- The editor watches the file. Edits you make on disk while `reverb edit` is open reload and re-run in the browser.

## Workflow for an agent

1. Write the file with the layout above, with readable ids on every cell, so diagnostics and `exec --json` point at
   meaningful names.
2. Run `reverb check nb.cs`, then `reverb exec nb.cs` (or `dotnet run nb.cs` when the tool isn't installed), and read
   the text output to verify the results.
3. Fix RVB findings by restructuring as the rules table says. Don't silence them.
4. Hand over with `reverb edit nb.cs` for interactive use, `reverb run` to share it as an app, or `reverb export --html`
   for a static report. In CI, run `reverb check`, then `reverb exec`, then `reverb export`.
