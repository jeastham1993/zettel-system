using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

public class ContentGenerationScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentGenerationScheduler> _logger;
    private readonly DayOfWeek _scheduledDay;
    private readonly TimeOnly _scheduledTime;

    public ContentGenerationScheduler(
        IServiceProvider serviceProvider,
        ILogger<ContentGenerationScheduler> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var dayStr = configuration["ContentGeneration:Schedule:DayOfWeek"] ?? "Monday";
        _scheduledDay = Enum.TryParse<DayOfWeek>(dayStr, ignoreCase: true, out var day)
            ? day
            : DayOfWeek.Monday;

        var timeStr = configuration["ContentGeneration:Schedule:TimeOfDay"] ?? "09:00";
        _scheduledTime = TimeOnly.TryParse(timeStr, out var time)
            ? time
            : new TimeOnly(9, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRun = ComputeNextRun(DateTime.UtcNow);
        _logger.LogInformation(
            "Content generation scheduler started. Next run: {NextRun:u} ({Day} at {Time})",
            nextRun, _scheduledDay, _scheduledTime);

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
                    "Scheduled content generation failed. Will retry at next scheduled time");
            }

            nextRun = ComputeNextRun(DateTime.UtcNow);
            _logger.LogInformation("Next scheduled content generation: {NextRun:u}", nextRun);
        }
    }

    private async Task RunGenerationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduled content generation");

        using var scope = _serviceProvider.CreateScope();
        var topicDiscovery = scope.ServiceProvider.GetRequiredService<ITopicDiscoveryService>();
        var contentGeneration = scope.ServiceProvider.GetRequiredService<IContentGenerationService>();

        var cluster = await topicDiscovery.DiscoverTopicAsync(cancellationToken);
        if (cluster is null)
        {
            _logger.LogWarning(
                "Scheduled generation skipped: no eligible notes for topic discovery");
            return;
        }

        var generation = await contentGeneration.GenerateContentAsync(cluster, cancellationToken);

        _logger.LogInformation(
            "Scheduled generation completed: {GenerationId} ({PieceCount} pieces)",
            generation.Id, generation.Pieces.Count);

        ZettelTelemetry.ScheduledGenerations.Add(1);
    }

    private DateTime ComputeNextRun(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var candidate = today.ToDateTime(_scheduledTime, DateTimeKind.Utc);

        // If we haven't passed the scheduled time today and today is the right day
        if (today.DayOfWeek == _scheduledDay && now < candidate)
            return candidate;

        // Find the next occurrence of the scheduled day
        var daysUntilTarget = (int)_scheduledDay - (int)today.DayOfWeek;
        if (daysUntilTarget <= 0)
            daysUntilTarget += 7;

        var nextDate = today.AddDays(daysUntilTarget);
        return nextDate.ToDateTime(_scheduledTime, DateTimeKind.Utc);
    }
}
