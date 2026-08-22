using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public sealed record UserSettingsDtoV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))]
    DayOfWeek WeekStart,
    string TimeZone,
    string TimeFormat,
    string DateFormat,
    bool SendNewsletter,
    bool WeeklyUpdates,
    bool LongRunning,
    bool ScheduledReports,
    bool Approval,
    bool Pto,
    bool Alerts,
    bool Reminders,
    bool TimeTrackingManual,
    SummaryReportSettingsDtoV1? SummaryReportSettings,
    bool IsCompactViewOn,
    string DashboardSelection,
    string DashboardViewType,
    bool DashboardPinToTop,
    int ProjectListCollapse,
    bool CollapseAllProjectLists,
    bool GroupSimilarEntriesDisabled,
    TimeOnly MyStartOfDay,
    bool ProjectPickerTaskFilter,
    [property: JsonPropertyName("lang")] string Language,
    bool MultiFactorEnabled,
    string Theme,
    bool Scheduling,
    bool Onboarding,
    bool InvoiceReminders,
    bool ShowOnlyWorkingDays
);
