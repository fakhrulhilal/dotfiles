namespace Dotfiles.Models.Clockify;

public sealed record GetWorkspaceResponse(
    string Id,
    string Name,
    WorkspaceSettingsDtoV1? WorkspaceSettings
);
