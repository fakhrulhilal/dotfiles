using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotfiles.Models;

public sealed class IpAddressJsonConverter : JsonConverter<IPAddress> {
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.String) {
            var ipString = reader.GetString();
            if (IPAddress.TryParse(ipString, out var ip)) return ip;
        }

        throw new JsonException(
            $"The JSON value '{reader.GetString()}' could not be converted to System.Net.IPAddress.");
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
