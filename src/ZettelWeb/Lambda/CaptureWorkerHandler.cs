using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.DependencyInjection;
using ZettelWeb.Services;

namespace ZettelWeb.Lambda;

/// <summary>
/// Processes inbound capture messages from the SQS queue.
/// Replaces SqsPollingBackgroundService — AWS manages the polling loop via
/// SQS event source mapping, Lambda receives pre-batched messages.
///
/// Batch size: 10 (configured in Terraform aws_lambda_event_source_mapping).
/// On partial failure, failed message IDs are returned so SQS can retry them.
/// </summary>
public class CaptureWorkerHandler
{
    public async Task<SQSBatchResponse> HandleAsync(SQSEvent sqsEvent, ILambdaContext context)
    {
        context.Logger.LogInformation(
            "Capture worker invoked with {Count} messages", sqsEvent.Records.Count);

        var services = LambdaServiceProvider.Build();
        await using var scope = services.CreateAsyncScope();
        var captureService = scope.ServiceProvider.GetRequiredService<CaptureService>();

        var failures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var source = record.MessageAttributes.TryGetValue("source", out var attr)
                    ? attr.StringValue ?? "unknown"
                    : "unknown";

                await captureService.CaptureAsync(record.Body, source);

                context.Logger.LogInformation(
                    "Processed capture message {MessageId} from {Source}",
                    record.MessageId, source);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex,
                    "Failed to process capture message {MessageId}: {Error}",
                    record.MessageId, ex.Message);

                // Return the failed message ID so SQS retries it (up to maxReceiveCount)
                failures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = record.MessageId
                });
            }
        }

        return new SQSBatchResponse(failures);
    }
}
