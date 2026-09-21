#!/usr/bin/env -S dotnet --

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property AssemblyName=dotclock
#:property EnumGenerator_EnumMetadataSource=DescriptionAttribute

#:include ./helpers/HttpHelper.cs
#:include ./helpers/ClockHelper.cs
#:include ./models/clockify/*.cs
#:include ./models/Duration.cs
#:include ./models/Result.cs

#:package ConsoleAppFramework@5.7.13
#:package NetEscapades.EnumGenerators@1.0.0-beta21*
#:package Spectre.Console@0.57.2

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleAppFramework;
using Dotfiles.Helpers;
using Dotfiles.Models;
using Dotfiles.Models.Clockify;
using NetEscapades.EnumGenerators;
using Spectre.Console;
using static Dotfiles.Helpers.HttpHelper;
using static Helper;

ConsoleApp.Create().Run(args);
return 0;

internal static class Const {
    public const int MaxParallelism = 5;
    public const int PageSize = 1000;
}

[RegisterCommands]
internal sealed class Commands {
    /// <summary>
    ///     Bulk add time entries from a JSON file.
    /// </summary>
    /// <param name="path">Path to JSON file.</param>
    /// <param name="day">Default day when not specified in the JSON file. Default to today.</param>
    /// <param name="apiKey">Clockify API key. Fallback to env CLOCKIFY_API_KEY.</param>
    /// <param name="apiUrl">Clockify API URL. Fallback to env CLOCKIFY_API_URL.</param>
    /// <param name="cancellationToken"></param>
    [Command("bulk-add")]
    public async Task<int> BulkAddEntry(
        string path,
        [HideDefaultValue] DateOnly? day = null,
        [HideDefaultValue] string? apiKey = null,
        [HideDefaultValue] string? apiUrl = null,
        CancellationToken cancellationToken = default) {
        var now = DateTime.Now;
        var validationResult = ValidateAndGetEntries();
        if (validationResult is not Result<ValidationCodes>.Success<AddTimeEntry[]> validationSuccess) {
            AnsiConsole.MarkupLine($"[red]ERROR[/]: {validationResult.Error.ToStringFast(true)}");
            return 1;
        }

        using var client = BuildHttpClient(apiUrl) ??
                           throw new InvalidOperationException("Unable to build API client");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var infoResult = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Getting information", async ctx => await GetInformation(ctx));
        if (infoResult is not Result<GetInfoCodes>.Success<GetProjectResponse[]> infoSuccess) {
            AnsiConsole.MarkupLine($"[red]ERROR[/]: {infoResult.Error.ToStringFast(true)}");
            return 1;
        }

        var table = new Table().Border(TableBorder.Simple)
            .AddColumn("Project", x => x.LeftAligned().Width(75))
            .AddColumn("Task", x => x.LeftAligned().Width(60))
            .AddColumn("Description", x => x.LeftAligned().Width(30))
            .AddColumn("Time", x => x.LeftAligned().Width(50))
            .AddColumn("Status", x => x.LeftAligned().Width(30));
        day ??= DateOnly.FromDateTime(now);
        var totalDuration = Duration.Empty;
        await AnsiConsole.Live(table).StartAsync(async ctx => {
            var projects = infoSuccess.Value.ToFrozenDictionary(x => x.Name, x => x, CompareMode);
            var parallelOptions = new ParallelOptions {
                MaxDegreeOfParallelism = Const.MaxParallelism, CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(validationSuccess.Value, parallelOptions, async (entry, token) => {
                if (token.IsCancellationRequested) return;

                var (start, end, time) = GetDuration(entry, day.Value);
                if (!projects.TryGetValue(entry.Project, out var project)) {
                    await AddAndRefresh(entry.Project, entry.Task, entry.Description, time, "Project not found");
                    return;
                }

                if (!project.Tasks.TryGetValue(entry.Task, out var task)) {
                    await AddAndRefresh(entry.Project, entry.Task, entry.Description, time, "Task not found");
                    return;
                }

                var addEntryDto = new PostTimeEntryRequest {
                    Description = entry.Description,
                    ProjectId = project.Id,
                    TaskId = task.Id,
                    Start = start.ToUniversalTime(),
                    End = end.ToUniversalTime(),
                    TagIds = null,
                    CustomFields = null,
                    CustomAttributes = null,
                    Billable = false,
                    Type = TimeEntryType.Regular
                };
                var status = "❌";
                var addResult = await client.AddTimeEntry(addEntryDto);
                if (addResult is not null && !string.IsNullOrEmpty(addResult.Id)) {
                    if (Duration.TryParse(addResult.TimeInterval?.Duration, out var duration)) {
                        totalDuration += duration;
                        status = $"{duration.Display} ✅";
                    }
                    else
                        status = "✅";
                }

                await AddAndRefresh(entry.Project, entry.Task, entry.Description, time, status);
            });

            async Task AddAndRefresh(string project, string task, string description, string time, string status) {
                table.AddRow(
                    Markup.Escape(project),
                    Markup.Escape(task),
                    Markup.Escape(description),
                    time, status);
                ctx.Refresh();
                await UiDelay();
            }
        });
        AnsiConsole.MarkupLine($"Total logged: [green]{totalDuration.Display}[/]");
        return 0;

        Result<ValidationCodes>.WithValue<AddTimeEntry[]> ValidateAndGetEntries() {
            day ??= DateOnly.FromDateTime(now.Date);
            if (string.IsNullOrEmpty(path)) return ValidationCodes.FileUnset;
            if (!File.Exists(path)) return ValidationCodes.FileNotFound;

            apiKey ??= Environment.GetEnvironmentVariable("CLOCKIFY_API_KEY");
            if (string.IsNullOrEmpty(apiKey)) return ValidationCodes.ApiKeyUnset;

            apiUrl ??= Environment.GetEnvironmentVariable("CLOCKIFY_API_URL");
            if (string.IsNullOrEmpty(apiUrl)) return ValidationCodes.ApiUrlUnset;

            try {
                using var file = File.OpenRead(path);
                return JsonSerializer.Deserialize(file, JsonOpt.Default.AddTimeEntryArray) is { Length: > 0 } parsed
                    ? parsed
                    : ValidationCodes.BlankEntry;
            }
            catch (PathTooLongException) { return ValidationCodes.FileTooDeep; }
            catch (UnauthorizedAccessException) { return ValidationCodes.FileNotFound; }
            catch (JsonException) { return ValidationCodes.InvalidJsonFile; }
        }

        async Task<Result<GetInfoCodes>.WithValue<GetProjectResponse[]>> GetInformation(StatusContext ctx) {
            ctx.Status("Getting user information");
            if (await client.GetUserInfo() is not { } userInfo ||
                string.IsNullOrEmpty(userInfo.ActiveWorkspace))
                return GetInfoCodes.UserNotFound;

            ctx.Status("Getting project information");
            WorkspaceId = userInfo.ActiveWorkspace;
            var entries = validationSuccess.Value;
            var projectNames = entries.Select(x => x.Project).ToHashSet(CompareMode);
            var projects = await client.GetProjectByNames(projectNames, cancellationToken);
            return projects is not { Length: > 0 } ? GetInfoCodes.ProjectsNotFound : projects;
        }
    }

    /// <summary>
    ///     Show time entry report per working day, grouped by project, task, and client.
    /// </summary>
    /// <param name="period">today, yesterday, this-week, last-week, this-month, last-month. "_" is accepted as well, e.g. this_week.</param>
    /// <param name="apiKey">Clockify API key. Fallback to env CLOCKIFY_API_KEY.</param>
    /// <param name="apiUrl">Clockify API URL. Fallback to env CLOCKIFY_API_URL.</param>
    /// <param name="reportUrl">Clockify report API URL, it's hosted separately from API URL. Fallback to env CLOCKIFY_REPORT_URL, then derived from API URL.</param>
    /// <param name="cancellationToken"></param>
    [Command("timesheet")]
    public async Task<int> Timesheet(
        [Argument] string period = "this-week",
        [HideDefaultValue] string? apiKey = null,
        [HideDefaultValue] string? apiUrl = null,
        [HideDefaultValue] string? reportUrl = null,
        CancellationToken cancellationToken = default) {
        var validationResult = ValidateAndGetPeriod();
        if (validationResult is not Result<ValidationCodes>.Success<TimesheetPeriod> validationSuccess) {
            AnsiConsole.MarkupLine($"[red]ERROR[/]: {validationResult.Error.ToStringFast(true)}");
            return 1;
        }

        using var client = BuildHttpClient(apiUrl) ??
                           throw new InvalidOperationException("Unable to build API client");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        using var reportClient = BuildHttpClient(reportUrl) ??
                                 throw new InvalidOperationException("Unable to build report API client");
        reportClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var infoResult = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync("Getting information", async ctx => await GetInformation(ctx));
        if (infoResult is not Result<GetInfoCodes>.Success<Timesheet> infoSuccess) {
            AnsiConsole.MarkupLine($"[red]ERROR[/]: {infoResult.Error.ToStringFast(true)}");
            return 1;
        }

        var timesheet = infoSuccess.Value;
        if (timesheet.Days is []) {
            AnsiConsole.MarkupLine(
                $"[yellow]WARNING[/]: No working day between {timesheet.Start:ddd, d MMM yyyy} and {timesheet.End:ddd, d MMM yyyy}");
            return 0;
        }

        var isMonthly = validationSuccess.Value is TimesheetPeriod.ThisMonth or TimesheetPeriod.LastMonth;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var table = new Table().Border(TableBorder.Simple)
            .Title($"Timesheet {timesheet.Start:ddd, d MMM yyyy} - {timesheet.End:ddd, d MMM yyyy}",
                new Style(decoration: Decoration.Bold))
            .AddColumn("Project - Task - [grey]Client[/]", x => x.LeftAligned().Footer("[bold]Total[/]"));
        var grandTotal = Duration.Empty;
        foreach (var day in timesheet.Days) {
            var dayTotal = timesheet.Rows.Aggregate(Duration.Empty,
                (total, row) => row.Durations.TryGetValue(day, out var duration) ? total + duration : total);
            grandTotal += dayTotal;
            var header = isMonthly ? $"{day:ddd}\n{day:%d}" : $"{day:ddd}\n{day:d MMM}";
            table.AddColumn(day == today ? $"[blue]{header}[/]" : header, x => {
                x.RightAligned().NoWrap().Footer(dayTotal.Display is "" ? "[grey]0:00[/]" : dayTotal.HourDisplay);
                // a month has ~22 working days, no gap is needed to fit in the screen
                if (isMonthly) x.PadLeft(0).PadRight(0);
                else x.PadLeft(2).PadRight(0);
            });
        }

        table.AddColumn("Total", x => x.RightAligned().NoWrap().PadLeft(isMonthly ? 2 : 3)
            .Footer($"[bold green]{grandTotal.HourDisplay}[/]"));
        foreach (var row in timesheet.Rows) {
            var names = new List<string>(3);
            if (!string.IsNullOrEmpty(row.Project))
                names.Add(row.ProjectColor is { } hex && Color.TryFromHex(hex, out var color)
                    ? $"[{color.ToMarkup()}]{Markup.Escape(row.Project)}[/]"
                    : Markup.Escape(row.Project));
            if (!string.IsNullOrEmpty(row.Task)) names.Add(Markup.Escape(row.Task));
            names.Add($"[grey]{Markup.Escape(row.Client)}[/]");
            var cells = new List<string>(timesheet.Days.Length + 2) { string.Join(" - ", names) };
            cells.AddRange(timesheet.Days.Select(day =>
                row.Durations.TryGetValue(day, out var duration) ? duration.HourDisplay : string.Empty));
            cells.Add($"[bold]{row.Total.HourDisplay}[/]");
            table.AddRow(cells.ToArray());
        }

        AnsiConsole.Write(table);
        return 0;

        Result<ValidationCodes>.WithValue<TimesheetPeriod> ValidateAndGetPeriod() {
            // match against description, so this_week is treated as this-week
            if (!TimesheetPeriod.TryParse(period.Replace('_', '-'), out var parsedPeriod, true, true))
                return ValidationCodes.InvalidPeriod;

            apiKey ??= Environment.GetEnvironmentVariable("CLOCKIFY_API_KEY");
            if (string.IsNullOrEmpty(apiKey)) return ValidationCodes.ApiKeyUnset;

            apiUrl ??= Environment.GetEnvironmentVariable("CLOCKIFY_API_URL");
            if (string.IsNullOrEmpty(apiUrl)) return ValidationCodes.ApiUrlUnset;

            reportUrl ??= Environment.GetEnvironmentVariable("CLOCKIFY_REPORT_URL") ?? BuildReportUrl(apiUrl);
            return string.IsNullOrEmpty(reportUrl) ? ValidationCodes.ReportUrlUnset : parsedPeriod;
        }

        async Task<Result<GetInfoCodes>.WithValue<Timesheet>> GetInformation(StatusContext ctx) {
            ctx.Status("Getting user information");
            if (await client.GetUserInfo() is not { } userInfo ||
                string.IsNullOrEmpty(userInfo.ActiveWorkspace))
                return GetInfoCodes.UserNotFound;

            ctx.Status("Getting workspace and time entry report");
            WorkspaceId = userInfo.ActiveWorkspace;
            var (start, end) = GetDateRange(validationSuccess.Value, DateOnly.FromDateTime(DateTime.Now),
                userInfo.Settings.WeekStart);
            var workspaceTask = client.GetWorkspace();
            var reportTask = reportClient.GetDetailedReport(
                new PostDetailedReportRequest {
                    DateRangeStart = start.StartOfDay().ToUniversalTime(),
                    DateRangeEnd = end.EndOfDay().ToUniversalTime(),
                    TimeZone = TimeZoneInfo.Utc.Id,
                    ExportType = ExportType.Json,
                    Users = new([userInfo.Id]),
                    DetailedFilter = new(1, Const.PageSize, new(TotalsOption.Exclude))
                }, cancellationToken);
            if (await workspaceTask is not { WorkspaceSettings.WorkingDays: { } workingDays })
                return GetInfoCodes.WorkspaceNotFound;

            var entries = await reportTask;
            var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
                .Select(start.AddDays)
                .Where(day => workingDays.Contains(day.DayOfWeek))
                .ToArray();
            return new Timesheet(start, end, days, GroupByDayAndProjectAndTask(entries, days));
        }

        TimesheetRow[] GroupByDayAndProjectAndTask(TimeEntryDto[] entries, DateOnly[] days) {
            var workingDays = days.ToFrozenSet();
            return entries
                .Select(entry => (Entry: entry, Day: DateOnly.FromDateTime(entry.TimeInterval.Start.ToLocalTime())))
                .Where(x => workingDays.Contains(x.Day))
                .GroupBy(x => (
                    Project: x.Entry.ProjectName ?? string.Empty,
                    ProjectColor: x.Entry.ProjectColor,
                    Task: x.Entry.TaskName ?? string.Empty,
                    Client: x.Entry.ClientName ?? string.Empty))
                .Select(group => new TimesheetRow(
                    group.Key.Project,
                    group.Key.ProjectColor,
                    group.Key.Task,
                    group.Key.Client,
                    group.GroupBy(x => x.Day, x => x.Entry.TimeInterval.Duration ?? 0)
                        .ToFrozenDictionary(x => x.Key, x => Duration.FromSeconds(x.Sum()))))
                .OrderBy(row => row.Project)
                .ThenBy(row => row.Task)
                .ToArray();
        }
    }
}

file static class Helper {
    private const StringComparison CompareMode2 = StringComparison.InvariantCultureIgnoreCase;
    public static readonly IEqualityComparer<string> CompareMode = StringComparer.InvariantCultureIgnoreCase;
    public static string WorkspaceId { get; set; } = string.Empty;
    private static readonly ConcurrentDictionary<string, GetProjectResponse> ProjectCaches = new(CompareMode);
    public static Task UiDelay() => Task.Delay(50);

    public static TimeRange GetDuration(AddTimeEntry entry, DateOnly defaultDay) {
        var start = new DateTimeOffset(new DateTime(entry.Day ?? defaultDay, entry.Start, DateTimeKind.Local));
        var end = new DateTimeOffset(new DateTime(entry.Day ?? defaultDay, entry.End, DateTimeKind.Local));
        return new(start, end, $"{start:ddd, d MMM yyyy HH:mm} - {end:HH:mm}");
    }

    public static (DateOnly Start, DateOnly End) GetDateRange(TimesheetPeriod period, DateOnly today,
        DayOfWeek weekStart) {
        var startOfWeek = today.AddDays(-((7 + (int)today.DayOfWeek - (int)weekStart) % 7));
        var startOfMonth = today.StartOfMonth();
        return period switch {
            TimesheetPeriod.Today => (today, today),
            TimesheetPeriod.Yesterday => (today.Yesterday(), today.Yesterday()),
            TimesheetPeriod.ThisWeek => (startOfWeek, startOfWeek.AddDays(6)),
            TimesheetPeriod.LastWeek => (startOfWeek.AddDays(-7), startOfWeek.AddDays(-1)),
            TimesheetPeriod.ThisMonth => (startOfMonth, startOfMonth.EndOfMonth()),
            TimesheetPeriod.LastMonth => (startOfMonth.AddMonths(-1), startOfMonth.AddDays(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unsupported period")
        };
    }

    /// <summary>
    ///     Report API is hosted differently: https://api.clockify.me/api/v1 => https://reports.api.clockify.me/v1,
    ///     regional/subdomain https://euc1.clockify.me/api/v1 => https://euc1.clockify.me/report/v1
    /// </summary>
    public static string? BuildReportUrl(string apiUrl) {
        const string globalApiHost = "api.clockify.me";
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)) return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.StartsWith("/api", CompareMode2)) path = path[4..];
        return uri.Host.Equals(globalApiHost, CompareMode2)
            ? $"{uri.Scheme}://reports.{uri.Authority}{path}"
            : $"{uri.Scheme}://{uri.Authority}/report{path}";
    }

    extension(HttpClient client) {
        public async ValueTask<GetUserInfoResponse?> GetUserInfo() =>
            await client.Get("/user", JsonOpt.Default.GetUserInfoResponse);

        public async ValueTask<PostTimeEntryResponse?> AddTimeEntry(PostTimeEntryRequest request) {
            return await client.Post($"/workspaces/{WorkspaceId}/time-entries", request,
                JsonOpt.Default.PostTimeEntryRequest, JsonOpt.Default.PostTimeEntryResponse);
        }

        public async ValueTask<GetProjectResponse[]> GetProjectByNames(IEnumerable<string> names,
            CancellationToken cancellationToken = default) {
            var results = new ConcurrentBag<GetProjectResponse>();
            var options = new ParallelOptions {
                MaxDegreeOfParallelism = Const.MaxParallelism, CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(names, options, async (name, _) => {
                var result = await client.GetProjectByName(name);
                if (result is not null) results.Add(result);
            });
            return results.ToArray();
        }

        private async ValueTask<GetProjectResponse?> GetProjectByName(string name) {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (ProjectCaches.TryGetValue(name, out var dto)) return dto;

            var encodedName = System.Web.HttpUtility.UrlEncode(name);
            var projects = await client.Get($"/workspaces/{WorkspaceId}/projects?name={encodedName}&page-size=1&page=1",
                JsonOpt.Default.GetProjectResponseArray);
            if (projects is not { Length: > 0 } ||
                projects.FirstOrDefault(p => name.Equals(p.Name, CompareMode2)) is not { } result)
                return null;

            var tasks = await client.GetProjectTasks(result.Id);
            result.Tasks = tasks.ToFrozenDictionary(x => x.Name, x => x, CompareMode);
            ProjectCaches.TryAdd(result.Name, result);
            return result;
        }

        private async ValueTask<GetTaskResponse[]> GetProjectTasks(string projectId) =>
            await client.Get($"/workspaces/{WorkspaceId}/projects/{projectId}/tasks",
                JsonOpt.Default.GetTaskResponseArray) ?? [];

        public async ValueTask<GetWorkspaceResponse?> GetWorkspace() =>
            await client.Get($"/workspaces/{WorkspaceId}", JsonOpt.Default.GetWorkspaceResponse);

        /// <summary>
        /// Get all time entries from a detailed report, walking through all pages
        /// </summary>
        public async ValueTask<TimeEntryDto[]> GetDetailedReport(PostDetailedReportRequest request,
            CancellationToken cancellationToken = default) {
            var results = new List<TimeEntryDto>();
            while (!cancellationToken.IsCancellationRequested) {
                var response = await client.Post($"/workspaces/{WorkspaceId}/reports/detailed", request,
                    JsonOpt.Default.PostDetailedReportRequest, JsonOpt.Default.PostDetailedReportResponse);
                if (response?.TimeEntries is not { Length: > 0 } entries) break;

                results.AddRange(entries);
                if (entries.Length < request.DetailedFilter.PageSize) break;

                request.DetailedFilter = request.DetailedFilter with { Page = request.DetailedFilter.Page + 1 };
            }

            return results.ToArray();
        }
    }
}

[JsonSerializable(typeof(GetUserInfoResponse))]
[JsonSerializable(typeof(AddTimeEntry[]))]
[JsonSerializable(typeof(PostTimeEntryRequest))]
[JsonSerializable(typeof(PostTimeEntryResponse))]
[JsonSerializable(typeof(GetProjectResponse[]))]
[JsonSerializable(typeof(GetTaskResponse[]))]
[JsonSerializable(typeof(GetWorkspaceResponse))]
[JsonSerializable(typeof(PostDetailedReportRequest))]
[JsonSerializable(typeof(PostDetailedReportResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowTrailingCommas = true)]
internal sealed partial class JsonOpt : JsonSerializerContext;

internal abstract class TimeEntryBase {
    public string Project { get; set; } = null!;
    public string Task { get; set; } = null!;
}

internal sealed class AddTimeEntry : TimeEntryBase {
    public string Description { get; set; } = null!;
    public DateOnly? Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

public sealed record TimeRange(DateTimeOffset Start, DateTimeOffset End, string Display);

internal sealed record Timesheet(DateOnly Start, DateOnly End, DateOnly[] Days, TimesheetRow[] Rows);

internal sealed record TimesheetRow(
    string Project,
    string? ProjectColor,
    string Task,
    string Client,
    IReadOnlyDictionary<DateOnly, Duration> Durations) {
    public Duration Total => Durations.Values.Aggregate(Duration.Empty, (total, duration) => total + duration);
}

[EnumExtensions]
enum TimesheetPeriod {
    [Description("today")]
    Today,

    [Description("yesterday")]
    Yesterday,

    [Description("this-week")]
    ThisWeek,

    [Description("last-week")]
    LastWeek,

    [Description("this-month")]
    ThisMonth,

    [Description("last-month")]
    LastMonth
}

[EnumExtensions]
enum GetInfoCodes {
    None,

    [Description("Unable to get user info")]
    UserNotFound,

    [Description("Unable to get projects info")]
    ProjectsNotFound,

    [Description("Unable to get workspace info")]
    WorkspaceNotFound
}

[EnumExtensions]
enum ValidationCodes {
    None,

    [Description("File path is not set")]
    FileUnset,

    [Description("File is not found or not readable")]
    FileNotFound,

    [Description("File is located in very deep nested folder")]
    FileTooDeep,

    [Description("JSON file might contain invalid format")]
    InvalidJsonFile,

    [Description("No entries found in JSON file")]
    BlankEntry,

    [Description("Clockify API key is not set")]
    ApiKeyUnset,

    [Description("Clockify API URL is not set")]
    ApiUrlUnset,

    [Description("Clockify report API URL is not set")]
    ReportUrlUnset,

    [Description("Period must be one of: today, yesterday, this-week, last-week, this-month, last-month")]
    InvalidPeriod
}
