using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

// Telegram webhook registration:
// https://api.telegram.org/bot<token>/setWebhook?url=https://<domain>/api/capture/telegram

/// <summary>Webhook endpoints for capturing notes via email and Telegram.</summary>
[ApiController]
[Route("api/capture")]
[EnableRateLimiting("capture")]
[Produces("application/json")]
public class CaptureController : ControllerBase
{
    private readonly CaptureService _captureService;
    private readonly CaptureConfig _config;
    private readonly ITelegramNotifier _telegramNotifier;
    private readonly ILogger<CaptureController> _logger;

    public CaptureController(
        CaptureService captureService,
        IOptions<CaptureConfig> config,
        ITelegramNotifier telegramNotifier,
        ILogger<CaptureController> logger)
    {
        _captureService = captureService;
        _config = config.Value;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    /// <summary>Receive an email webhook payload to capture as a fleeting note.</summary>
    /// <remarks>Requires a valid X-Webhook-Secret header. Rate limited to 10 requests per minute.</remarks>
    [HttpPost("email")]
    [ProducesResponseType(200)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> EmailWebhook([FromBody] JsonElement payload)
    {
        if (string.IsNullOrEmpty(_config.WebhookSecret))
        {
            _logger.LogWarning("Email webhook rejected: WebhookSecret not configured");
            return Ok();
        }

        var headerValue = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        if (!string.Equals(headerValue, _config.WebhookSecret, StringComparison.Ordinal))
        {
            _logger.LogWarning("Email webhook rejected: invalid or missing X-Webhook-Secret header");
            return Ok();
        }

        var (content, isValid) = _captureService.ParseEmailPayload(payload);

        if (!isValid)
            return Ok();

        await _captureService.CaptureAsync(content, "email");

        return Ok();
    }

    /// <summary>Receive a Telegram webhook payload to capture as a fleeting note.</summary>
    /// <remarks>Requires a valid X-Telegram-Bot-Api-Secret-Token header. Rate limited to 10 requests per minute.</remarks>
    [HttpPost("telegram")]
    [ProducesResponseType(200)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> TelegramWebhook([FromBody] JsonElement payload)
    {
        if (string.IsNullOrEmpty(_config.TelegramBotToken))
        {
            _logger.LogWarning("Telegram webhook rejected: TelegramBotToken not configured");
            return Ok();
        }

        var headerValue = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(headerValue, _config.TelegramBotToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Telegram webhook rejected: invalid or missing secret token header");
            return Ok();
        }

        var (content, isValid, chatId) = _captureService.ParseTelegramUpdate(payload);

        if (!isValid)
            return Ok();

        await _captureService.CaptureAsync(content, "telegram");
        await _telegramNotifier.SendAsync(chatId, "✅ Note saved.");

        return Ok();
    }
}
