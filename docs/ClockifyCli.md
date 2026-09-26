# ClockifyCli (`dotclock`)

A [Clockify](https://clockify.me) client for logging time in bulk and reading your timesheet from the terminal.
Source: [`csharp/ClockifyCli.cs`](../csharp/ClockifyCli.cs), no need to jump in to website if this all you want.

```
dotclock <command> [options]
```

| Command     | Purpose                                                                     |
|-------------|-----------------------------------------------------------------------------|
| `bulk-add`  | Add many time entries at once from a JSON file                              |
| `timesheet` | Show your time entries per working day, grouped by project, task and client |

## Configuration

Both commands need an API key and the API URL. Pass them as options or set the environment variables. Options win.

| Option         | Environment variable  | Notes                                        |
|----------------|-----------------------|----------------------------------------------|
| `--api-key`    | `CLOCKIFY_API_KEY`    | Create it in Clockify: Profile settings, API |
| `--api-url`    | `CLOCKIFY_API_URL`    | For example `https://api.clockify.me/api/v1` |
| `--report-url` | `CLOCKIFY_REPORT_URL` | `timesheet` only. Derived from the API URL   |

Clockify hosts reports on a separate URL. When `--report-url` is not given it is derived from the API URL:

- `https://api.clockify.me/api/v1` becomes `https://reports.api.clockify.me/v1`
- `https://euc1.clockify.me/api/v1` becomes `https://euc1.clockify.me/report/v1`

```shell
export CLOCKIFY_API_KEY=<your key>
export CLOCKIFY_API_URL=https://api.clockify.me/api/v1
```

## `bulk-add`

Reads a JSON array of entries and creates each one in your active workspace, up to 5 at a time. A live table shows every
entry with its result, followed by the total time logged.

```shell
dotclock bulk-add --path entries.json
dotclock bulk-add --path entries.json --day 2026-09-25
```

| Option   | Description                                                                        |
|----------|------------------------------------------------------------------------------------|
| `--path` | JSON file to read. Required                                                        |
| `--day`  | Day (`yyyy-MM-dd`) for entries that do not have their own `day`. Defaults to today |

### JSON file

```json
[
  {
    "project": "Internal",
    "task": "Meetings",
    "description": "Sprint planning",
    "start": "09:00",
    "end": "10:00"
  },
  {
    "project": "Payment System",
    "task": "Development",
    "description": "Bug fix: integration with Stripe",
    "day": "2026-09-24",
    "start": "13:00",
    "end": "17:00"
  }
]
```

- `project` and `task` are matched by name, ignoring case. The task must belong to the project.
- `start` and `end` are local times, so daylight saving and your machine's time zone apply.
- `day` is optional. An entry without one uses `--day`, and then today.
- Trailing commas are accepted.
- Entries are created as regular, non-billable time entries without tags.

The status column of the table tells what happened to each entry: the logged duration with a check mark when it was
created, `Project not found` or `Task not found` when the name did not match, and a cross when Clockify rejected it.
Nothing is created for an entry that shows a problem, and the command still exits with `0`, so read the table.

## `timesheet`

Prints one row per project, task and client and one column per working day of your workspace, with day and grand totals.
Today's column is highlighted.

```shell
dotclock timesheet               # this-week
dotclock timesheet last-week
dotclock timesheet this_month
```

The period is a positional argument and defaults to `this-week`:

| Period       | Range                                                                              |
|--------------|------------------------------------------------------------------------------------|
| `today`      | Today                                                                              |
| `yesterday`  | Yesterday                                                                          |
| `this-week`  | The 7 days from the week start set in your Clockify profile, including future days |
| `last-week`  | The 7 days before that                                                             |
| `this-month` | The first to the last day of the current month                                     |
| `last-month` | The first to the last day of the previous month                                    |

Underscores work as well, so `this_week` is the same as `this-week`.