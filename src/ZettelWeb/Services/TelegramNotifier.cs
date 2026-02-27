using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>
/// Sends outbound Telegram messages using the Telegram Bot API.
/// Uses the bot token and allowlisted chat IDs from CaptureConfig.
/// All failure paths are logged and swallowed — notifications are best-effort.
/// </summary>
public class TelegramNotifier : ITelegramNotifier
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly long[] _chatIds;
    private readonly ILogger<TelegramNotifier> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TelegramNotifier(
        IHttpClientFactory httpFactory,
        IOptions<CaptureConfig> config,
        ILogger<TelegramNotifier> logger)
    {
        _http = httpFactory.CreateClient("Telegram");
        _token = config.Value.TelegramBotToken;
        _chatIds = config.Value.AllowedTelegramChatIds;
        _logger = logger;
    }

    public async Task BroadcastAsync(string message, CancellationToken ct = default)
    {
        foreach (var chatId in _chatIds)
            await SendAsync(chatId, message, ct);
    }

    public async Task SendAsync(long chatId, string message, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_token}/sendMessage";
            var payload = JsonSerializer.Serialize(new { chat_id = chatId, text = message });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Telegram sendMessage returned {StatusCode} for chat {ChatId}",
                    (int)response.StatusCode, chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram notification failed for chat {ChatId}", chatId);
        }
    }
}
