using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

/// <summary>
/// Base class for content generation schedulers.
/// Subclasses define which mediums to generate and when to run next.
/// </summary>
public abstract class ContentSchedulerBase : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    protected ContentSchedulerBase(IServiceProvider serviceProvider, ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>The content mediums this scheduler generates (e.g. ["blog"] or ["social"]).</summary>
    protected abstract IReadOnlyList<string> Mediums { get; }

    /// <summary>Returns the next UTC DateTime this scheduler should run.</summary>
    public abstract DateTime ComputeNextRun(DateTime now);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRun = ComputeNextRun(DateTime.UtcNow);
        _logger.LogInformation(
            "{Scheduler} started. Next run: {NextRun:u}",
            GetType().Name, nextRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = nextRun - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            try
            {
                await RunGenerationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Scheduler} generation failed. Will retry at next scheduled time",
                    GetType().Name);
            }

            nextRun = ComputeNextRun(DateTime.UtcNow);
            _logger.LogInformation(
                "{Scheduler} next run: {NextRun:u}", GetType().Name, nextRun);
        }
    }

    private async Task RunGenerationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Scheduler} starting generation for mediums: {Mediums}",
            GetType().Name, string.Join(", ", Mediums));

        using var scope = _serviceProvider.CreateScope();
        var topicDiscovery = scope.ServiceProvider.GetRequiredService<ITopicDiscoveryService>();
        var contentGeneration = scope.ServiceProvider.GetRequiredService<IContentGenerationService>();

        var cluster = await topicDiscovery.DiscoverTopicAsync(cancellationToken);
        if (cluster is null)
        {
            _logger.LogWarning(
                "{Scheduler} skipped: no eligible notes for topic discovery",
                GetType().Name);
            return;
        }

        var generation = await contentGeneration.GenerateContentAsync(
            cluster, Mediums, cancellationToken);

        _logger.LogInformation(
            "{Scheduler} completed: {GenerationId} ({PieceCount} pieces)",
            GetType().Name, generation.Id, generation.Pieces.Count);

        ZettelTelemetry.ScheduledGenerations.Add(1);
    }
}
