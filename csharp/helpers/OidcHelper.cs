using System.Text.Json.Serialization;

namespace Dotfiles.Helpers;

public static class OidcHelper {
    
}

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union TokenResponse(TokenResponse.Error, TokenResponse.Success) {
    public sealed record Error(
        [property: JsonPropertyName("error")] string Code,
        string ErrorDescription);

    public sealed record Success(
        int ExpiresIn,
        string TokenType,
        string AccessToken,
        string? RefreshToken,
        string? IdToken);
}

public record TokenCache(string RefreshToken, DateTimeOffset SavedAt);

[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(TokenCache))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class JsonOidc : JsonSerializerContext;