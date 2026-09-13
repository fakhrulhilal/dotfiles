using System.Text.Json.Serialization;

namespace Dotfiles.Models.Clockify;

public enum ExportType {
    Json,

    [JsonStringEnumMemberName("JSON_V1")]
    JsonV1,
    Pdf,
    Csv,
    Xlsx,
    Zip
}
