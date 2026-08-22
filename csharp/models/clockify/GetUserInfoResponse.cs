namespace Dotfiles.Models.Clockify;

public sealed record GetUserInfoResponse(
    string Id,
    string Email,
    string Name,
    string ProfilePicture,
    string ActiveWorkspace,
    string DefaultWorkspace,
    UserSettingsDtoV1 Settings
);
