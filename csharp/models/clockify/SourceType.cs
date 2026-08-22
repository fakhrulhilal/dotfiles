using System.Text.Json.Serialization;

public enum SourceType {
    Workspace = 1,
    Project = 2,

    [JsonStringEnumMemberName("TIMEENTRY")]
    TimeEntry = 3
}
