using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using ZettelWeb.Background;

namespace ZettelWeb.Lambda;

/// <summary>
/// Processes pending note embeddings.
/// Triggered every 60 seconds by EventBridge Scheduler.
///
/// Calls the public methods already on EmbeddingBackgroundService — no changes
/// to that class are required. The Lambda handler is purely additive.
/// </summary>
public class EmbeddingWorkerHandler
{
    public async Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("Embedding worker invoked");

        var services = LambdaServiceProvider.Build();
        await using var scope = services.CreateAsyncScope();

        var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingBackgroundService>();

        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(10));

        // Reset any notes stuck in Processing state (e.g. from a previous Lambda timeout)
        await embeddingService.RecoverProcessingNotesAsync(cts.Token);

        var pendingIds = await embeddingService.GetPendingNoteIdsAsync(cts.Token);

        context.Logger.LogInformation(
            "Found {Count} notes pending embedding", pendingIds.Count);

        foreach (var noteId in pendingIds)
        {
            if (cts.Token.IsCancellationRequested) break;
            await embeddingService.ProcessNoteAsync(noteId, cts.Token);
        }

        context.Logger.LogInformation("Embedding worker completed");
    }
}
