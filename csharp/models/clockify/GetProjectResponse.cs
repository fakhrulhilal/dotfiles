using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public sealed record GetProjectResponse(
    string Id,
    string Name,
    string ClientId,
    string ClientName,
    string WorkspaceId,
    bool Billable,
    bool Archived,
    bool Public) {
    [JsonIgnore]
    public FrozenDictionary<string, GetTaskResponse> Tasks { get; set; } = null!;
}
