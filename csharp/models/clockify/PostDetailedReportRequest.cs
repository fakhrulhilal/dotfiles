namespace Dotfiles.Models.Clockify;

public sealed class PostDetailedReportRequest {
    /// <summary>
    /// Must be UTC, serialized as YYYY-MM-DDTHH:MM:SS.sssZ
    /// </summary>
    public required DateTime DateRangeStart { get; set; }

    /// <summary>
    /// Must be UTC, serialized as YYYY-MM-DDTHH:MM:SS.sssZ
    /// </summary>
    public required DateTime DateRangeEnd { get; set; }

    /// <summary>
    /// Time zone used to interpret date range and to format response dates
    /// </summary>
    public required string TimeZone { get; set; }

    public required ExportType ExportType { get; set; }
    public required ContainsUsersFilterV1 Users { get; set; }
    public required DetailedFilterV1 DetailedFilter { get; set; }
}
