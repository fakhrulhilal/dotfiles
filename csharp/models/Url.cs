using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Dotfiles.Models;

public sealed partial record Url(
    string? Scheme,
    string Host,
    int? Port,
    bool Secure,
    string? Path,
    string? Username,
    string? Password,
    IReadOnlyDictionary<string, string> Extras) {
    [GeneratedRegex(
        @"^((?<scheme>[a-zA-Z][a-zA-Z\+]*):\/\/)?(?:(?<username>[^:@\/\s]+)(?::(?<password>[^@\/\s]*))?@)?(?<host>[^:\/\s]+)(?::(?<port>\d+))?(?:\/(?<path>[^\?\s]*))?(?:\?(?<query>[^\s#]*))?$",
        RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    private static readonly Regex UrlRegex = UrlPattern();

    public static bool TryParse(string value, [NotNullWhen(true)] out Url? result) {
        if (UrlRegex.Match(value) is not { Success: true } match) {
            result = null;
            return false;
        }

        var compareMode = StringComparison.InvariantCulture;
        var scheme = match.Groups["scheme"].Success ? match.Groups["scheme"].Value : string.Empty;
        var secure = scheme switch {
            "https" or "wss" or "ftps" or "ssh" => true,
            "ssl" or "tls" => true,
            "secure" => true,
            _ when scheme.EndsWith("+ssl", compareMode) => true,
            _ when scheme.EndsWith("+tls", compareMode) => true,
            _ when scheme.EndsWith("+secure", compareMode) => true,
            _ => false
        };

        var (username, password) = (match.Groups["username"], match.Groups["password"]) switch {
            ({ Success: true, Value: var user }, { Success: true, Value: var pass }) =>
                (Uri.UnescapeDataString(user), Uri.UnescapeDataString(pass)),
            ({ Success: true, Value: var user }, _) => (Uri.UnescapeDataString(user), null),
            _ => (null, null)
        };
        var extras = new Dictionary<string, string>();
        if (match.Groups["query"] is { Success: true, Value: var queryString } && !string.IsNullOrEmpty(queryString)) {
            var parsedQuery = System.Web.HttpUtility.ParseQueryString(queryString);
            foreach (var key in parsedQuery.AllKeys.Where(key => key != null))
                extras[key!] = parsedQuery[key!]!;
        }

        if (extras.TryGetValue("secure", out var secureValue)) {
            if (bool.TryParse(secureValue, out var parsedSecure))
                secure = parsedSecure;
            else if (int.TryParse(secureValue, out var intSecure))
                secure = intSecure != 0;
            extras.Remove("secure");
        }

        result = new(
            scheme, match.Groups["host"].Value,
            match.Groups["port"].Success ? int.Parse(match.Groups["port"].Value) : null,
            secure, match.Groups["path"].Success ? match.Groups["path"].Value : null,
            username, password, extras);
        return true;
    }
}
