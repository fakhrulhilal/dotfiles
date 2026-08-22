namespace Dotfiles.Models.Clockify;

public sealed record CustomFieldValueDtoV1(
    string CustomFieldId,
    string Name,
    string TimeEntryId,
    string Type,
    object? Value
);
