using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Dotfiles.Models;

/// <summary>
/// ISO 8601 Duration
/// </summary>
/// <param name="Years">Total years</param>
/// <param name="Months">Total months</param>
/// <param name="Days">Total days</param>
/// <param name="Hours">Total hours</param>
/// <param name="Minutes">Total minutes</param>
/// <param name="Seconds">Total seconds</param>
/// <param name="Display">Human readable format</param>
/// <param name="Value">Original ISO 8601 format</param>
public sealed partial record Duration(
    int? Years,
    int? Months,
    int? Days,
    int? Hours,
    int? Minutes,
    int? Seconds,
    string Display,
    string Value
) {
    private static readonly Regex DurationRegex = DurationPattern();

    public static Duration Empty => new(null, null, null, null, null, null, string.Empty, "PT");

    public static bool TryParse(string? input, [NotNullWhen(true)] out Duration? result) {
        if (string.IsNullOrWhiteSpace(input)) {
            result = null;
            return false;
        }

        var match = DurationRegex.Match(input);
        if (!match.Success) {
            result = null;
            return false;
        }

        int? year = null, month = null, day = null, hour = null, minute = null, second = null;
        Calculate(ref year, "year");
        Calculate(ref month, "month");
        Calculate(ref day, "day");
        Calculate(ref hour, "hour");
        Calculate(ref minute, "minute");
        Calculate(ref second, "second");
        var display = BuildDisplay(year, month, day, hour, minute, second);
        result = new(year, month, day, hour, minute, second, display, input.Trim());
        return true;

        void Calculate(ref int? value, string groupName) {
            if (match.Groups[groupName].Success) value = int.Parse(match.Groups[groupName].Value);
        }
    }

    public override string ToString() => Value;

    public static Duration operator +(Duration left, Duration right) {
        int? upper = null;
        var second = Add(left.Seconds, right.Seconds, 60);
        var minute = Add(left.Minutes, right.Minutes, 60);
        var hour = Add(left.Hours, right.Hours, 24);
        var day = Add(left.Days, right.Days, 31);
        var month = Add(left.Months, right.Months, 12);
        var year = Add(left.Years, right.Years, 1);
        var isoFormat = BuildIsoFormat(year, month, day, hour, minute, second);
        var display = BuildDisplay(year, month, day, hour, minute, second);
        return new Duration(year, month, day, hour, minute, second, display, isoFormat);

        int? Add(int? a, int? b, int boundary) {
            var total = (a ?? 0) + (b ?? 0) + (upper ?? 0);
            upper = total / boundary;
            var current = total % boundary;
            return current > 0 ? current : null;
        }
    }

    private static string BuildIsoFormat(int? year, int? month, int? day, int? hour, int? minute, int? second) {
        var builder = new StringBuilder(5 * 6);
        builder.Append('P');
        Append(year, 'Y');
        Append(month, 'M');
        Append(day, 'D');
        builder.Append('T');
        Append(hour, 'H');
        Append(minute, 'M');
        Append(second, 'S');
        return builder.ToString();

        void Append(int? value, char suffix) {
            if (value.HasValue) builder.Append($"{value}{suffix}");
        }
    }

    private static string BuildDisplay(int? year, int? month, int? day, int? hour, int? minute, int? second) {
        var builder = new StringBuilder(15 * 6);
        Append(year, "year");
        Append(month, "month");
        Append(day, "day");
        Append(hour, "hour");
        Append(minute, "minute");
        Append(second, "second");
        return builder.ToString().Trim();

        void Append(int? value, string suffix) {
            switch (value) {
                case > 1: builder.Append($"{value} {suffix}s "); break;
                case >= 0: builder.Append($"{value} {suffix} "); break;
            }
        }
    }

    [GeneratedRegex(
        @"^P((?<year>\d+)Y)?((?<month>\d+)M)?((?<day>\d+)D)?T((?<hour>\d+)H)?((?<minute>\d+)M)?((?<second>\d+)S)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-ID")]
    private static partial Regex DurationPattern();
}
