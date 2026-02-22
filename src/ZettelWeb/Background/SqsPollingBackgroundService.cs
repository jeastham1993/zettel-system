using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

public class SqsPollingBackgroundService : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SqsPollingBackgroundService> _logger;
    private readonly string _queueUrl;

    public DateTime? LastPollUtc { get; private set; }

    public SqsPollingBackgroundService(
        IAmazonSQS sqsClient,
        IServiceProvider serviceProvider,
        ILogger<SqsPollingBackgroundService> logger,
        IConfiguration configuration)
    {
        _sqsClient = sqsClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _queueUrl = configuration["Capture:SqsQueueUrl"] ?? "";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SQS polling started for queue {QueueUrl}", _queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20,
                    MessageSystemAttributeNames = ["All"],
                    MessageAttributeNames = ["source"],
                };

                var response = await _sqsClient.ReceiveMessageAsync(request, stoppingToken);
                LastPollUtc = DateTime.UtcNow;

                foreach (var message in response.Messages ?? [])
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        await ProcessMessageAsync(message, stoppingToken);

                        await _sqsClient.DeleteMessageAsync(
                            _queueUrl, message.ReceiptHandle, stoppingToken);

                        _logger.LogInformation(
                            "Processed and deleted SQS message {MessageId}", message.MessageId);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to process SQS message {MessageId}, leaving for retry",
                            message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling SQS queue");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("SQS polling stopped");
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var source = message.MessageAttributes?.TryGetValue("source", out var attr) == true
            ? attr.StringValue
            : null;

        var payload = JsonDocument.Parse(message.Body).RootElement;

        // Detect SES notifications delivered via SNS -> SQS (no source attribute)
        if (string.IsNullOrEmpty(source) &&
            payload.TryGetProperty("notificationType", out var notifType) &&
            notifType.GetString() == "Received")
        {
            source = "ses";
        }

        if (string.IsNullOrEmpty(source))
        {
            _logger.LogWarning(
                "SQS message {MessageId} has no source attribute and is not an SES notification, skipping",
                message.MessageId);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var captureService = scope.ServiceProvider.GetRequiredService<CaptureService>();

        switch (source.ToLowerInvariant())
        {
            case "ses":
            {
                var (content, isValid) = captureService.ParseSesNotification(payload);
                if (!isValid)
                {
                    _logger.LogWarning(
                        "SQS SES message {MessageId} failed validation", message.MessageId);
                    return;
                }
                await captureService.CaptureAsync(content, "email");
                break;
            }
            case "email":
            {
                var (content, isValid) = captureService.ParseEmailPayload(payload);
                if (!isValid)
                {
                    _logger.LogWarning(
                        "SQS email message {MessageId} failed validation", message.MessageId);
                    return;
                }
                await captureService.CaptureAsync(content, "email");
                break;
            }
            case "telegram":
            {
                var (content, isValid) = captureService.ParseTelegramUpdate(payload);
                if (!isValid)
                {
                    _logger.LogWarning(
                        "SQS telegram message {MessageId} failed validation", message.MessageId);
                    return;
                }
                await captureService.CaptureAsync(content, "telegram");
                break;
            }
            default:
                _logger.LogWarning(
                    "SQS message {MessageId} has unknown source '{Source}', skipping",
                    message.MessageId, source);
                break;
        }
    }
}
