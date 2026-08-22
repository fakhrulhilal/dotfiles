namespace Dotfiles.Models.Clockify;

public sealed class PostTimeEntryRequest {
    public required bool Billable { get; set; }
    public CreateCustomAttributeRequest[]? CustomAttributes { get; set; }
    public UpdateCustomFieldRequest[]? CustomFields { get; set; }
    public required string Description { get; set; }
    public required DateTimeOffset Start { get; set; }
    public required DateTimeOffset End { get; set; }
    public required string ProjectId { get; set; }
    public required string TaskId { get; set; }
    public List<string>? TagIds { get; set; }
    public required TimeEntryType Type { get; set; }
}
