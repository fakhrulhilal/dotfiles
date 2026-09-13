using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public sealed record WorkspaceSettingsDtoV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))]
    DayOfWeek WeekStart,
    DayOfWeek[]? WorkingDays
);
