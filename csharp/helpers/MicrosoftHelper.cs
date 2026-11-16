#!/usr/bin/env dotnet --

#:include ../models/OidcDto.cs

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dotfiles.Models;

namespace Dotfiles.Helpers;

public class MicrosoftHelper {
}

public sealed class MicrosoftSignIn(
    HttpClient http,
    string clientId,
    string username,
    TimeSpan answerTimeout) {
    const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    const string RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient";
    const string Scope = "https://outlook.office.com/IMAP.AccessAsUser.All offline_access";
    const string AnswerPrefix = "auth answer:";

    static readonly string CacheFile = Path.Combine(
        Exporter.Env("XDG_STATE_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
        "export_attachment",
        "token.json");

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) {
        if (ReadCachedRefreshToken() is { } refreshToken) {
            var refreshed =
                await RequestTokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, },
                    cancellationToken);
            if (refreshed.AccessToken is not null)
                return Remember(refreshed);

            Console.WriteLine($"Cached sign-in is no longer valid ({refreshed.Error}); signing in again.");
        }

        return Remember(await SignInAsync(cancellationToken));
    }

    async Task<TokenResponse> SignInAsync(CancellationToken cancellationToken) {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var authorizeUrl = $"{Authority}/authorize?client_id={Uri.EscapeDataString(clientId)}"
                           + $"&response_type=code&response_mode=query&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                           + $"&scope={Uri.EscapeDataString(Scope)}&login_hint={Uri.EscapeDataString(username)}&state={state}"
                           + $"&code_challenge={Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))}&code_challenge_method=S256";
        var answer = await AskAsync(authorizeUrl, cancellationToken);
        var code = CodeFromAnswer(answer, state);
        var token = await RequestTokenAsync(new() {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }, cancellationToken);
        return token.AccessToken is not null
            ? token
            : throw new InvalidOperationException($"Microsoft sign-in failed: {token.Error} {token.ErrorDescription}");
    }

    async Task<string> AskAsync(string authorizeUrl, CancellationToken cancellationToken) {
        var steps =
            $"🔐 export_attachment needs you to sign in to Outlook ({username}).\n\n"
            + $"1. Open: {authorizeUrl}\n"
            + "2. After signing in you land on a blank page. Copy its full URL.\n";
        if (telegram.IsConfigured) {
            Console.WriteLine("No valid sign-in cached; asked for it on Telegram.");
            var question =
                steps + $"3. Reply here within {answerTimeout.TotalHours:0.#} hours: {AnswerPrefix} <the URL>";
            return await telegram.AskAsync(question, AnswerPrefix, answerTimeout, cancellationToken);
        }

        if (Console.IsInputRedirected)
            throw new InvalidOperationException(
                "No valid sign-in cached and Telegram is not configured (TELEGRAM_BOT_TOKEN/TELEGRAM_CHAT_ID).");

        Console.WriteLine(steps + "3. Paste the URL below:");
        return Console.ReadLine() ?? string.Empty;
    }

    static string CodeFromAnswer(string answer, string expectedState) {
        answer = answer.Trim();
        if (!answer.Contains("code=", StringComparison.Ordinal))
            return answer;

        var query = answer[(answer.IndexOf('?') + 1)..].Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);
        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException(
                $"Microsoft sign-in failed: {error} {query.GetValueOrDefault("error_description")}");
        if (query.TryGetValue("state", out var state) && state != expectedState)
            throw new InvalidOperationException("Sign-in answer belongs to another sign-in request (state mismatch).");

        return query["code"];
    }

    async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken) {
        form["client_id"] = clientId;
        form["scope"] = Scope;
        using var response =
            await http.PostAsync($"{Authority}/token", new FormUrlEncodedContent(form), cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(body, JsonOidc.Default.TokenResponse, cancellationToken)
               ?? throw new InvalidOperationException($"Empty token response ({(int)response.StatusCode}).");
    }

    static string? ReadCachedRefreshToken() =>
        File.Exists(CacheFile)
            ? JsonSerializer.Deserialize(File.ReadAllText(CacheFile), JsonOidc.Default.TokenCache)?.RefreshToken
            : null;

    static string Remember(TokenResponse token) {
        if (token.RefreshToken is { } refreshToken) {
            var directory = Path.GetDirectoryName(CacheFile)!;
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var json = JsonSerializer.Serialize(new TokenCache(refreshToken, DateTimeOffset.UtcNow),
                JsonOidc.Default.TokenCache);
            var temp = CacheFile + ".tmp";
            File.WriteAllText(temp, json);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, CacheFile, overwrite: true);
        }

        return token.AccessToken!;
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
