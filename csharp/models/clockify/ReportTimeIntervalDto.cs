namespace Dotfiles.Models.Clockify;

/// <param name="Start">UTC start time</param>
/// <param name="End">UTC end time, empty for running timer</param>
/// <param name="Duration">Duration in seconds, empty for running timer</param>
public sealed record ReportTimeIntervalDto(
    DateTime Start,
    DateTime? End,
    long? Duration
);
