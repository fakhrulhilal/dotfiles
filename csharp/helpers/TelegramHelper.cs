#!/usr/bin/env dotnet --

#:include ../models/Result.cs
#:include ../models/TelegramDto.cs

#:property EnumGenerator_EnumMetadataSource=DescriptionAttribute

#:package NetEscapades.EnumGenerators@1.0.0-beta21*

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dotfiles.Models;
using NetEscapades.EnumGenerators;

namespace Dotfiles.Helpers;

public static class TelegramHelper {
    public static Result<TelegramCodes>.WithValue<TelegramClient> BuildClient(
        string? botToken = null, string? chatId = null, int? timeoutSeconds = null) {
        if (EnvFallback(botToken, "TELEGRAM_BOT_TOKEN") is not { } telegramToken) return TelegramCodes.InvalidBotToken;
        if (EnvFallback(chatId, "TELEGRAM_CHAT_ID") is not { } telegramChatId) return TelegramCodes.InvalidChatId;

        var httpClient = new HttpClient { BaseAddress = new Uri($"https://api.telegram.org/bot{telegramToken}") };
        if (timeoutSeconds.HasValue) httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds.Value);
        return new TelegramClient(httpClient, telegramChatId);
    }

    private static string? EnvFallback(string? value, string env) => value ?? Environment.GetEnvironmentVariable(env);
}

public sealed class TelegramClient(HttpClient http, string chatId) : IDisposable {
    private bool _disposed;

    public async Task SendAsync(string text, CancellationToken cancellationToken) {
        using var response = await http.PostAsync("/sendMessage",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["chat_id"] = chatId, ["text"] = text, ["link_preview_options"] = """{"is_disabled":true}""",
            }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<TelegramUpdate>> GetUpdatesAsync(
        long offset, int timeoutSeconds, CancellationToken cancellationToken) {
        await using var body = await http.GetStreamAsync(
            $"/getUpdates?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D",
            cancellationToken);
        var response =
            await JsonSerializer.DeserializeAsync(body, JsonTelegram.Default.TelegramUpdates, cancellationToken);
        return response?.Result ?? [];
    }

    public async Task<long> NextUpdateOffsetAsync(CancellationToken cancellationToken) {
        var latest = await GetUpdatesAsync(offset: -1, timeoutSeconds: 0, cancellationToken);
        return latest.Count > 0 ? latest[^1].UpdateId + 1 : 0;
    }

    public async Task<Result<TelegramCodes>.WithValue<string>> AskAsync(
        string question, string answerPrefix, TimeSpan timeout, CancellationToken cancellationToken) {
        var offset = await NextUpdateOffsetAsync(cancellationToken);
        await SendAsync(question, cancellationToken);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            List<TelegramUpdate> updates;
            try {
                updates = await GetUpdatesAsync(offset, timeoutSeconds: 50, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                       !cancellationToken.IsCancellationRequested) {
                await Console.Error.WriteLineAsync($"Telegram poll failed, retrying: {ex.Message}", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                continue;
            }

            foreach (var update in updates) {
                offset = update.UpdateId + 1;
                var text = update.Message?.Text?.Trim();
                if (update.Message?.Chat.Id.ToString(CultureInfo.InvariantCulture) != chatId
                    || text is null
                    || !text.StartsWith(answerPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                await GetUpdatesAsync(offset, timeoutSeconds: 0, cancellationToken);
                // await SendAsync("👍 Got it, signing in.", cancellationToken);
                return text[answerPrefix.Length..].Trim();
            }
        }

        return TelegramCodes.NoAnswer;
    }

    public void Dispose() {
        if (_disposed) return;

        http.Dispose();
        _disposed = true;
    }
}

[JsonSerializable(typeof(TelegramMessage))]
[JsonSerializable(typeof(TelegramUpdates))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class JsonTelegram : JsonSerializerContext;

[EnumExtensions]
public enum TelegramCodes {
    None,

    [Description("Telegram bot token is not set (env: TELEGRAM_BOT_TOKEN)")]
    InvalidBotToken,

    [Description("Telegram chat ID is not set (env: TELEGRAM_CHAT_ID)")]
    InvalidChatId,

    [Description("No answer after period of waiting")]
    NoAnswer
}
