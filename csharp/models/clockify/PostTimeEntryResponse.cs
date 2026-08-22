using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public sealed record PostTimeEntryResponse(
    bool Billable,
    [property: JsonPropertyName("customFieldValues")] CustomFieldValueDtoV1[]? CustomFields,
    string Description,
    string Id,
    bool IsLocked,
    string KiosId,
    string ProjectId,
    string TaskId,
    List<string>? TagIds,
    TimeEntryType Type,
    string UserId,
    string WorkspaceId,
    TimeIntervalDtoV1? TimeInterval
);
