namespace Dotfiles.Models.Clockify;

public sealed record ContainsUsersFilterV1(
    string[] Ids,
    ContainsType Contains = ContainsType.Contains,
    UserStatus Status = UserStatus.All
);
