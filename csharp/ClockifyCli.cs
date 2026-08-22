#!/usr/bin/env -S dotnet --

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true
#:property AssemblyName=clock#
#:property EnumGenerator_EnumMetadataSource=DescriptionAttribute

#:include ./helpers/HttpHelper.cs
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
                    else {
                        status = "✅";
                    }
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
    }
}

[JsonSerializable(typeof(GetUserInfoResponse))]
[JsonSerializable(typeof(AddTimeEntry[]))]
[JsonSerializable(typeof(PostTimeEntryRequest))]
[JsonSerializable(typeof(PostTimeEntryResponse))]
[JsonSerializable(typeof(GetProjectResponse[]))]
[JsonSerializable(typeof(GetTaskResponse[]))]
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

[EnumExtensions]
enum GetInfoCodes {
    None,

    [Description("Unable to get user info")]
    UserNotFound,

    [Description("Unable to get projects info")]
    ProjectsNotFound
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
    ApiUrlUnset
}
