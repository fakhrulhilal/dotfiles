namespace Dotfiles.Models.Clockify;

public sealed record TimeIntervalDtoV1(
    string Duration,
    DateTimeOffset Start,
    DateTimeOffset End
);
