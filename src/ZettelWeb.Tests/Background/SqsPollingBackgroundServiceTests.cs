using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Endpoints;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Background;

public class SqsPollingBackgroundServiceTests
{
    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/test-queue";

    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ZettelDbContext>(o =>
            o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEmbeddingQueue, ChannelEmbeddingQueue>();
        services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();
        services.Configure<CaptureConfig>(c =>
        {
            c.AllowedEmailSenders = ["james@example.com"];
            c.AllowedTelegramChatIds = [12345];
        });
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<CaptureService>();
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(string queueUrl = TestQueueUrl)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Capture:SqsQueueUrl"] = queueUrl,
            })
            .Build();
    }

    private static SqsPollingBackgroundService CreateService(
        FakeSqsClient sqsClient,
        ServiceProvider sp,
        IConfiguration? config = null)
    {
        return new SqsPollingBackgroundService(
            sqsClient,
            sp,
            NullLogger<SqsPollingBackgroundService>.Instance,
            config ?? BuildConfig());
    }

    private static Message CreateSqsMessage(string source, object body, string? messageId = null)
    {
        var msg = new Message
        {
            MessageId = messageId ?? Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = JsonSerializer.Serialize(body),
        };
        msg.MessageAttributes["source"] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = source,
        };
        return msg;
    }

    /// <summary>
    /// Start the service, wait for FakeSqsClient to signal that the first batch
    /// of messages has been processed, then stop cleanly.
    /// </summary>
    private static async Task RunServiceUntilProcessed(
        SqsPollingBackgroundService service,
        FakeSqsClient sqsClient)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(cts.Token);
        await sqsClient.WaitForProcessingAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessesEmailMessage_AndDeletesAfterPersist()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var emailPayload = new { from = "james@example.com", subject = "Test", text = "Hello from SQS" };
        var sqsMessage = CreateSqsMessage("email", emailPayload);
        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Verify note was persisted
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Single(notes);
        Assert.Contains("Hello from SQS", notes[0].Content);
        Assert.Equal("email", notes[0].Source);

        // Verify message was deleted
        Assert.Single(sqsClient.DeletedReceiptHandles);
        Assert.Equal(sqsMessage.ReceiptHandle, sqsClient.DeletedReceiptHandles[0]);
    }

    [Fact]
    public async Task ProcessesTelegramMessage_AndDeletesAfterPersist()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var telegramPayload = new
        {
            message = new
            {
                chat = new { id = 12345 },
                text = "Hello from Telegram via SQS",
            }
        };
        var sqsMessage = CreateSqsMessage("telegram", telegramPayload);
        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Single(notes);
        Assert.Contains("Hello from Telegram via SQS", notes[0].Content);
        Assert.Equal("telegram", notes[0].Source);

        Assert.Single(sqsClient.DeletedReceiptHandles);
    }

    [Fact]
    public async Task DeletesMessage_WhenValidationRejects()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        // Invalid sender - CaptureService.ParseEmailPayload returns isValid=false
        var emailPayload = new { from = "hacker@evil.com", subject = "Test", text = "spam" };
        var sqsMessage = CreateSqsMessage("email", emailPayload);
        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Message is deleted because ProcessMessageAsync completed without throwing
        Assert.Single(sqsClient.DeletedReceiptHandles);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Empty(notes);
    }

    [Fact]
    public async Task DoesNotDeleteMessage_WhenCaptureAsyncThrows()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        // Malformed JSON will cause JsonDocument.Parse to throw
        var sqsMessage = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = "THIS IS NOT JSON",
        };
        sqsMessage.MessageAttributes["source"] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = "email",
        };

        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Message should NOT be deleted because processing threw
        Assert.Empty(sqsClient.DeletedReceiptHandles);
    }

    [Fact]
    public async Task SkipsMessage_WithMissingSourceAttribute()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var sqsMessage = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = JsonSerializer.Serialize(new { text = "no source" }),
        };
        // No "source" attribute set

        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Message with missing source is still deleted (processed without error)
        Assert.Single(sqsClient.DeletedReceiptHandles);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Empty(notes);
    }

    [Fact]
    public async Task SkipsMessage_WithUnknownSource()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var sqsMessage = CreateSqsMessage("slack", new { text = "unknown source" });
        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Unknown source is deleted (handled gracefully)
        Assert.Single(sqsClient.DeletedReceiptHandles);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Empty(notes);
    }

    [Fact]
    public async Task ProcessesSesNotification_WithoutSourceAttribute()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        // SES notification via SNS -> SQS (raw delivery) has no "source" attribute
        var sesPayload = new
        {
            notificationType = "Received",
            mail = new
            {
                source = "james@example.com",
                commonHeaders = new
                {
                    from = new[] { "james@example.com" },
                    subject = "SES Email Test"
                }
            },
            content = "From: james@example.com\r\nSubject: SES Email Test\r\nContent-Type: text/plain\r\n\r\nHello from SES"
        };

        // No source attribute -- simulates SNS -> SQS raw delivery
        var sqsMessage = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = JsonSerializer.Serialize(sesPayload),
        };

        var sqsClient = new FakeSqsClient([sqsMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Verify note was persisted with email source
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Single(notes);
        Assert.Contains("Hello from SES", notes[0].Content);
        Assert.Equal("email", notes[0].Source);

        // Verify message was deleted (processed successfully)
        Assert.Single(sqsClient.DeletedReceiptHandles);
    }

    [Fact]
    public async Task PoisonMessage_DoesNotKillPollLoop()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        // First message is poison (bad JSON), second is valid
        var poisonMessage = new Message
        {
            MessageId = "poison",
            ReceiptHandle = "poison-handle",
            Body = "NOT VALID JSON",
        };
        poisonMessage.MessageAttributes["source"] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = "email",
        };

        var validPayload = new { from = "james@example.com", subject = "Valid", text = "Valid message" };
        var validMessage = CreateSqsMessage("email", validPayload, "valid");

        var sqsClient = new FakeSqsClient([poisonMessage, validMessage]);

        var service = CreateService(sqsClient, sp);
        await RunServiceUntilProcessed(service, sqsClient);

        // Poison message should NOT be deleted
        Assert.DoesNotContain("poison-handle", sqsClient.DeletedReceiptHandles);

        // Valid message should be deleted
        Assert.Contains(validMessage.ReceiptHandle, sqsClient.DeletedReceiptHandles);

        // Valid note should be persisted
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var notes = await db.Notes.ToListAsync();
        Assert.Single(notes);
        Assert.Contains("Valid message", notes[0].Content);
    }

    [Fact]
    public async Task UpdatesLastPollUtc_AfterSuccessfulPoll()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var sqsClient = new FakeSqsClient([]);

        var service = CreateService(sqsClient, sp);
        Assert.Null(service.LastPollUtc);

        await RunServiceUntilProcessed(service, sqsClient);

        Assert.NotNull(service.LastPollUtc);
        Assert.True(service.LastPollUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GracefulShutdown_OnCancellation()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var sqsClient = new FakeSqsClient([]);

        var service = CreateService(sqsClient, sp);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await sqsClient.WaitForProcessingAsync(cts.Token);

        // Cancel and verify clean shutdown
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // If we get here without exception, graceful shutdown succeeded
    }
}

/// <summary>
/// Fake IAmazonSQS client for testing. Returns configured messages on first poll,
/// then signals processing complete (when the second ReceiveMessage is called,
/// meaning the service has finished all messages from the first batch).
/// </summary>
public class FakeSqsClient : IAmazonSQS
{
    private readonly List<Message> _messages;
    private bool _messagesReturned;
    private readonly TaskCompletionSource _processingComplete = new();

    public List<string> DeletedReceiptHandles { get; } = [];

    public FakeSqsClient(List<Message> messages)
    {
        _messages = messages;
    }

    /// <summary>
    /// Wait until the background service has processed all messages from the first batch.
    /// This completes when the service calls ReceiveMessageAsync a second time (meaning
    /// it has finished iterating through all messages from the first batch).
    /// </summary>
    public Task WaitForProcessingAsync(CancellationToken cancellationToken)
    {
        return _processingComplete.Task.WaitAsync(cancellationToken);
    }

    public Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_messagesReturned)
        {
            _messagesReturned = true;

            if (_messages.Count == 0)
            {
                // No messages to process - signal complete immediately
                _processingComplete.TrySetResult();
            }

            return Task.FromResult(new ReceiveMessageResponse
            {
                Messages = new List<Message>(_messages),
            });
        }

        // Second call means all first-batch messages have been processed
        _processingComplete.TrySetResult();

        // Throw OperationCanceledException to simulate long poll being cancelled
        // The service's outer catch handles this for graceful shutdown
        cancellationToken.ThrowIfCancellationRequested();

        // If not yet cancelled, return empty and let the loop continue
        return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
    }

    public Task<DeleteMessageResponse> DeleteMessageAsync(
        string queueUrl,
        string receiptHandle,
        CancellationToken cancellationToken = default)
    {
        DeletedReceiptHandles.Add(receiptHandle);
        return Task.FromResult(new DeleteMessageResponse());
    }

    // Not used in tests, but required by interface
    public void Dispose() { }

    public IClientConfig Config => throw new NotImplementedException();
    public ISQSPaginatorFactory Paginators => throw new NotImplementedException();
    public Task<Dictionary<string, string>> GetAttributesAsync(string queueUrl) => throw new NotImplementedException();
    public Task SetAttributesAsync(string queueUrl, Dictionary<string, string> attributes) => throw new NotImplementedException();
    public Task<string> AuthorizeS3ToSendMessageAsync(string queueUrl, string bucket) => throw new NotImplementedException();
    public Task<AddPermissionResponse> AddPermissionAsync(string queueUrl, string label, List<string> awsAccountIds, List<string> actions, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AddPermissionResponse> AddPermissionAsync(AddPermissionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CancelMessageMoveTaskResponse> CancelMessageMoveTaskAsync(CancelMessageMoveTaskRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(string queueUrl, string receiptHandle, int visibilityTimeout, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(ChangeMessageVisibilityRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(string queueUrl, List<ChangeMessageVisibilityBatchRequestEntry> entries, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(ChangeMessageVisibilityBatchRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreateQueueResponse> CreateQueueAsync(string queueName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreateQueueResponse> CreateQueueAsync(CreateQueueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteMessageResponse> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(string queueUrl, List<DeleteMessageBatchRequestEntry> entries, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(DeleteMessageBatchRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteQueueResponse> DeleteQueueAsync(string queueUrl, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteQueueResponse> DeleteQueueAsync(DeleteQueueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(string queueUrl, List<string> attributeNames, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(GetQueueAttributesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetQueueUrlResponse> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetQueueUrlResponse> GetQueueUrlAsync(GetQueueUrlRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListDeadLetterSourceQueuesResponse> ListDeadLetterSourceQueuesAsync(ListDeadLetterSourceQueuesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListMessageMoveTasksResponse> ListMessageMoveTasksAsync(ListMessageMoveTasksRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListQueuesResponse> ListQueuesAsync(string queueNamePrefix, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListQueuesResponse> ListQueuesAsync(ListQueuesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListQueueTagsResponse> ListQueueTagsAsync(ListQueueTagsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PurgeQueueResponse> PurgeQueueAsync(string queueUrl, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PurgeQueueResponse> PurgeQueueAsync(PurgeQueueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ReceiveMessageResponse> ReceiveMessageAsync(string queueUrl, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RemovePermissionResponse> RemovePermissionAsync(string queueUrl, string label, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RemovePermissionResponse> RemovePermissionAsync(RemovePermissionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SendMessageResponse> SendMessageAsync(string queueUrl, string messageBody, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SendMessageBatchResponse> SendMessageBatchAsync(string queueUrl, List<SendMessageBatchRequestEntry> entries, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SendMessageBatchResponse> SendMessageBatchAsync(SendMessageBatchRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(string queueUrl, Dictionary<string, string> attributes, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(SetQueueAttributesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<StartMessageMoveTaskResponse> StartMessageMoveTaskAsync(StartMessageMoveTaskRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<TagQueueResponse> TagQueueAsync(TagQueueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UntagQueueResponse> UntagQueueAsync(UntagQueueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Endpoint DetermineServiceOperationEndpoint(AmazonWebServiceRequest request) => throw new NotImplementedException();
#pragma warning disable CS0067 // Event is never used
    public event EventHandler<ExceptionEventArgs>? ExceptionEvent;
    public event EventHandler? BeforeRequestEvent;
    public event EventHandler? AfterResponseEvent;
#pragma warning restore CS0067
}
