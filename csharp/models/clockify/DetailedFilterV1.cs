namespace Dotfiles.Models.Clockify;

public sealed record DetailedFilterV1(
    int Page,
    int PageSize,
    DetailedOptionsV1 Options
);

public sealed record DetailedOptionsV1(
    TotalsOption Totals
);
