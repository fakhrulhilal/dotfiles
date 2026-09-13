namespace Dotfiles.Models.Clockify;

public sealed record TimeEntryDto(
    string? ProjectName,
    string? ProjectColor,
    string? TaskName,
    string? ClientName,
    ReportTimeIntervalDto TimeInterval
);
