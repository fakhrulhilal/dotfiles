namespace Dotfiles.Models.Clockify;

public sealed class CreateCustomAttributeRequest {
    public required string Name { get; set; }
    public required string Namespace { get; set; }
    public required string Value { get; set; }
}
