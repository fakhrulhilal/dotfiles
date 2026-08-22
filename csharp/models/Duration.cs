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

        var displayBuilder = new StringBuilder(15 * 6);
        int? year = null, month = null, day = null, hour = null, minute = null, second = null;
        if (match.Groups["year"].Success) {
            year = int.Parse(match.Groups["year"].Value);
            displayBuilder.Append($"{year} {(year is > 1 ? "years" : "year")} ");
        }

        if (match.Groups["month"].Success) {
            month = int.Parse(match.Groups["month"].Value);
            displayBuilder.Append($"{month} {(month is > 1 ? "months" : "month")} ");
        }

        if (match.Groups["day"].Success) {
            day = int.Parse(match.Groups["day"].Value);
            displayBuilder.Append($"{day} {(day is > 1 ? "days" : "day")} ");
        }

        if (match.Groups["hour"].Success) {
            hour = int.Parse(match.Groups["hour"].Value);
            displayBuilder.Append($"{hour} {(hour is > 1 ? "hours" : "hour")} ");
        }

        if (match.Groups["minute"].Success) {
            minute = int.Parse(match.Groups["minute"].Value);
            displayBuilder.Append($"{minute} {(minute is > 1 ? "minutes" : "minute")} ");
        }

        if (match.Groups["second"].Success) {
            second = int.Parse(match.Groups["second"].Value);
            displayBuilder.Append($"{second} {(second is > 1 ? "seconds" : "second")} ");
        }

        result = new(year, month, day, hour, minute, second, displayBuilder.ToString().Trim(), input.Trim());
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex(
        @"^P((?<year>\d+)Y)?((?<month>\d+)M)?((?<day>\d+)D)?T((?<hour>\d+)H)?((?<minute>\d+)M)?((?<second>\d+)S)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-ID")]
    private static partial Regex DurationPattern();
}
