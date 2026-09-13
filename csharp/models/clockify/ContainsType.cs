using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public enum ContainsType {
    Contains,

    [JsonStringEnumMemberName("DOES_NOT_CONTAIN")]
    DoesNotContain,

    [JsonStringEnumMemberName("CONTAINS_ONLY")]
    ContainsOnly
}
