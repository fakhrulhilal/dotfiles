namespace Dotfiles.Models;

public record TelegramUpdates(List<TelegramUpdate>? Result);

public record TelegramUpdate(long UpdateId, TelegramMessage? Message);

public record TelegramMessage(TelegramChat Chat, string? Text);

public record TelegramChat(long Id);
