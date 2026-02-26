using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using ZettelWeb.Services;

namespace ZettelWeb.Lambda;

/// <summary>
/// Runs the weekly content generation job.
/// Triggered by EventBridge Scheduler on the configured cron (default: Mondays 09:00 UTC).
///
/// Replaces ContentGenerationScheduler's RunGenerationAsync — the same service
/// layer calls, without the hosting loop.
/// </summary>
public class ContentScheduleHandler
{
    public async Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("Content schedule handler invoked");

        var services = LambdaServiceProvider.Build();
        await using var scope = services.CreateAsyncScope();

        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(30));

        var topicDiscovery = scope.ServiceProvider.GetRequiredService<ITopicDiscoveryService>();
        var contentGeneration = scope.ServiceProvider.GetRequiredService<IContentGenerationService>();

        var cluster = await topicDiscovery.DiscoverTopicAsync(cts.Token);
        if (cluster is null)
        {
            context.Logger.LogWarning(
                "Content generation skipped: no eligible notes for topic discovery");
            return;
        }

        var generation = await contentGeneration.GenerateContentAsync(cluster, cts.Token);

        context.Logger.LogInformation(
            "Content generation completed: {GenerationId} ({PieceCount} pieces)",
            generation.Id, generation.Pieces.Count);

        ZettelTelemetry.ScheduledGenerations.Add(1);
    }
}
