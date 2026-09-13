using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public sealed record PostDetailedReportResponse(
    [property: JsonPropertyName("timeentries")] TimeEntryDto[]? TimeEntries
);
