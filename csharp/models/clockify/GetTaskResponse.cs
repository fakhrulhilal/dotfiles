namespace Dotfiles.Models.Clockify;

public sealed record GetTaskResponse(
    string Id,
    string Name,
    string ProjectId,
    bool Billable
);
