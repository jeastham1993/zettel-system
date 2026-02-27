using ZettelWeb.Services;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// In-memory ITelegramNotifier for testing.
/// Records every message sent for assertion in tests.
/// </summary>
public class FakeTelegramNotifier : ITelegramNotifier
{
    public List<(long ChatId, string Message)> SentMessages { get; } = [];
    public List<string> BroadcastMessages { get; } = [];

    public Task BroadcastAsync(string message, CancellationToken ct = default)
    {
        BroadcastMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task SendAsync(long chatId, string message, CancellationToken ct = default)
    {
        SentMessages.Add((chatId, message));
        return Task.CompletedTask;
    }
}
