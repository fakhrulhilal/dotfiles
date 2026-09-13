using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public enum UserStatus {
    All,

    [JsonStringEnumMemberName("ACTIVE_WITH_PENDING")]
    ActiveWithPending,
    Active,
    Pending,
    Inactive
}
