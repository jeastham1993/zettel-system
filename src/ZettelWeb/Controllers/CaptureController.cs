using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

// Telegram webhook registration:
// https://api.telegram.org/bot<token>/setWebhook?url=https://<domain>/api/capture/telegram

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("capture")]
public class CaptureController : ControllerBase
{
    private readonly CaptureService _captureService;
    private readonly CaptureConfig _config;
    private readonly ILogger<CaptureController> _logger;

    public CaptureController(
        CaptureService captureService,
        IOptions<CaptureConfig> config,
        ILogger<CaptureController> logger)
    {
        _captureService = captureService;
        _config = config.Value;
        _logger = logger;
    }

    [HttpPost("email")]
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

    [HttpPost("telegram")]
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

        var (content, isValid) = _captureService.ParseTelegramUpdate(payload);

        if (!isValid)
            return Ok();

        await _captureService.CaptureAsync(content, "telegram");

        return Ok();
    }
}
