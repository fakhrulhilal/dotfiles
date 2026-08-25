using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dotfiles.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(LongText), "long_text")]
[JsonDerivedType(typeof(Bit), "boolean")]
[JsonDerivedType(typeof(Number), "number")]
[JsonDerivedType(typeof(FullTime), "date_time")]
[JsonDerivedType(typeof(Date), "date")]
[JsonDerivedType(typeof(Time), "time")]
public abstract class DbParameter {
    public static DbParameter Create(string name, string value,
        bool longText = false) => longText
        ? new LongText { Name = name, Value = value }
        : new Text { Name = name, Value = value };

    public static Json Create<T>(string name, T value, JsonTypeInfo<T> jsonConverter) =>
        new() { Name = name, Value = JsonSerializer.SerializeToDocument(value, jsonConverter) };

    public static Json Create(string name, JsonDocument value) => new() { Name = name, Value = value };

    public static Bit Create(string name, bool value) => new() { Name = name, Value = value };
    public static Ip Create(string name, IPAddress value) => new() { Name = name, Value = value };

    public required string Name { get; init; }

    public sealed class Text : DbParameter {
        public required string Value { get; init; }
    }

    public sealed class LongText : DbParameter {
        public required string Value { get; init; }
    }

    public sealed class Bit : DbParameter {
        public required bool Value { get; init; }
    }

    public sealed class Number : DbParameter {
        public required int Value { get; init; }
    }

    public sealed class FullTime : DbParameter {
        public required DateTime Value { get; init; }
    }

    public sealed class Date : DbParameter {
        public required DateOnly Value { get; init; }
    }

    public sealed class Time : DbParameter {
        public required TimeOnly Value { get; set; }
    }

    public sealed class Ip : DbParameter {
        public required IPAddress Value { get; set; }
    }

    public sealed class Json : DbParameter {
        public required JsonDocument Value { get; set; }
    }
}
