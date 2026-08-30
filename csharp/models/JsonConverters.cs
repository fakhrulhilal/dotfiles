using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

public sealed class RawJsonConverter : JsonConverter<string> {
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString() ?? string.Empty;

        throw new JsonException(
            $"The JSON value '{reader.GetString()}' could not be converted to System.Net.IPAddress.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value);
}

public static class JsonExtensions {
    public static void ApplyMergePatch<T>(this JsonDocument source, T target, JsonTypeInfo<T> typeInfo)
        where T : class {
        if (source is not { RootElement: { ValueKind: JsonValueKind.Object } root })
            return;

        root.ApplyMergePatch(target, typeInfo);
    }

    public static void ApplyMergePatch<T>(this JsonElement source, T target, JsonTypeInfo<T> typeInfo)
        where T : class =>
        source.ApplyMergePatchInternal(target, typeInfo);

    private static void ApplyMergePatchInternal(this JsonElement source, object target, JsonTypeInfo typeInfo) {
        foreach (var property in typeInfo.Properties) {
            if (!source.TryGetProperty(property.Name, out var element)) continue;

            var nullableType = Nullable.GetUnderlyingType(property.PropertyType);
            var targetType = nullableType ?? property.PropertyType;
            var propTypeInfo = typeInfo.Options.GetTypeInfo(targetType);
            switch (element.ValueKind) {
                case JsonValueKind.Null when nullableType is not null || property.IsSetNullable:
                    property.Set?.Invoke(target, null);
                    break;
                case JsonValueKind.Null when targetType == typeof(string):
                    property.Set?.Invoke(target, string.Empty);
                    break;
                case JsonValueKind.Null:
                    property.Set?.Invoke(target, null);
                    break;
                case JsonValueKind.Object when targetType.IsClass:
                    var subTarget = property.Get?.Invoke(target);
                    if (subTarget is null) {
                        // Use STJ's AOT-generated factory to instantiate the nested object
                        if (propTypeInfo.CreateObject != null) {
                            subTarget = propTypeInfo.CreateObject();
                            property.Set?.Invoke(target, subTarget);
                        }
                        else {
                            // Fallback: If it has no parameterless constructor, replace it entirely
                            var parsed = element.Deserialize(propTypeInfo);
                            property.Set?.Invoke(target, parsed);
                            continue;
                        }
                    }

                    element.ApplyMergePatchInternal(subTarget, propTypeInfo);
                    break;
                case JsonValueKind.String when targetType.IsEnum &&
                                               (property.CustomConverter?.CanConvert(targetType) ?? false):
                    if (Enum.TryParse(targetType, element.GetString(), true, out var stringEnum))
                        property.Set?.Invoke(target, stringEnum);
                    break;
                case JsonValueKind.String when targetType == typeof(string):
                    property.Set?.Invoke(target, element.GetString());
                    break;
                case JsonValueKind.Number:
                    if (targetType.IsEnum &&
                        Enum.TryParse(targetType, element.GetInt32().ToString(), out var numberEnum))
                        property.Set?.Invoke(target, numberEnum);
                    else if (targetType == typeof(int))
                        property.Set?.Invoke(target, element.GetInt32());
                    else if (targetType == typeof(long))
                        property.Set?.Invoke(target, element.GetInt64());
                    else if (targetType == typeof(double))
                        property.Set?.Invoke(target, element.GetDouble());
                    else if (targetType == typeof(decimal))
                        property.Set?.Invoke(target, element.GetDecimal());
                    break;
                case JsonValueKind.True or JsonValueKind.False when targetType == typeof(bool):
                    property.Set?.Invoke(target, element.GetBoolean());
                    break;
                case JsonValueKind.Undefined: break;
                default: {
                    var value = element.Deserialize(propTypeInfo);
                    property.Set?.Invoke(target, value);
                    break;
                }
            }
        }
    }
}
