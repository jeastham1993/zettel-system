using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using ZettelWeb.Services;

namespace ZettelWeb.Lambda;

/// <summary>Input accepted by the content schedule Lambda.</summary>
public class ContentScheduleInput
{
    /// <summary>
    /// Which medium to generate: "blog", "social", or "all" (default).
    /// Configure separate EventBridge Scheduler rules to invoke this handler
    /// with different inputs for independent cadences.
    ///
    /// Example EventBridge input for weekly blog:  {"Medium": "blog"}
    /// Example EventBridge input for daily social: {"Medium": "social"}
    /// </summary>
    public string Medium { get; set; } = "all";
}

/// <summary>
/// Runs content generation for the specified medium.
/// Triggered by EventBridge Scheduler on the configured cron.
///
/// Replaces ContentSchedulerBase.RunGenerationAsync — the same service
/// layer calls, without the hosting loop.
/// </summary>
public class ContentScheduleHandler
{
    public async Task HandleAsync(ContentScheduleInput input, ILambdaContext context)
    {
        var medium = input?.Medium ?? "all";
        context.Logger.LogInformation("Content schedule handler invoked for medium: {Medium}", medium);

        IReadOnlyList<string>? mediums = medium.ToLowerInvariant() switch
        {
            "blog" => ["blog"],
            "social" => ["social"],
            _ => null  // null = generate all mediums
        };

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

        var generation = await contentGeneration.GenerateContentAsync(cluster, mediums, cts.Token);

        context.Logger.LogInformation(
            "Content generation completed: {GenerationId} ({PieceCount} pieces, medium: {Medium})",
            generation.Id, generation.Pieces.Count, medium);

        ZettelTelemetry.ScheduledGenerations.Add(1);
    }
}
