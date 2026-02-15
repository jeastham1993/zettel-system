namespace ZettelWeb.Services;

public class CaptureConfig
{
    public string[] AllowedEmailSenders { get; set; } = [];
    public long[] AllowedTelegramChatIds { get; set; } = [];
    public string TelegramBotToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public int EnrichmentTimeoutSeconds { get; set; } = 10;
    public int EnrichmentMaxRetries { get; set; } = 3;
    public string SqsQueueUrl { get; set; } = "";
    public string SqsRegion { get; set; } = "";
}
