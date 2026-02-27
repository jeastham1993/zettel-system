namespace ZettelWeb.Services;

/// <summary>Sends outbound Telegram messages via the Telegram Bot API.</summary>
public interface ITelegramNotifier
{
    /// <summary>
    /// Send a message to every chat ID in AllowedTelegramChatIds.
    /// Best-effort: failures are logged and swallowed.
    /// </summary>
    Task BroadcastAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Send a message to a specific chat ID.
    /// Best-effort: failures are logged and swallowed.
    /// </summary>
    Task SendAsync(long chatId, string message, CancellationToken ct = default);
}

/// <summary>
/// No-op notifier used when TelegramBotToken is not configured.
/// Registered by default so callers need no null guards.
/// </summary>
public class NullTelegramNotifier : ITelegramNotifier
{
    public Task BroadcastAsync(string message, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendAsync(long chatId, string message, CancellationToken ct = default) => Task.CompletedTask;
}
