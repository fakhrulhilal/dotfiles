public sealed class UpdateCustomFieldRequest {
    public required string CustomFieldId { get; set; }
    public required SourceType SourceType { get; set; }
    public required object Value { get; set; }
}
