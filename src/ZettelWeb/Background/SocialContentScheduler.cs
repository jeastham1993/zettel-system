using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

/// <summary>
/// Scheduled background service that generates social media posts on a daily cadence.
/// Reads schedule from ContentGeneration:Schedule:Social (TimeOfDay only).
/// </summary>
public class SocialContentScheduler : ContentSchedulerBase
{
    private readonly TimeOnly _scheduledTime;

    protected override IReadOnlyList<string> Mediums => ["social"];

    public SocialContentScheduler(
        IServiceProvider serviceProvider,
        ILogger<SocialContentScheduler> logger,
        IConfiguration configuration,
        ITelegramNotifier notifier) : base(serviceProvider, logger, notifier)
    {
        var timeStr = configuration["ContentGeneration:Schedule:Social:TimeOfDay"] ?? "09:00";
        _scheduledTime = TimeOnly.TryParse(timeStr, out var time)
            ? time
            : new TimeOnly(9, 0);
    }

    /// <summary>Constructor for unit testing — accepts schedule values directly.</summary>
    public SocialContentScheduler(TimeOnly scheduledTime)
        : base(serviceProvider: null!, logger: null!, notifier: new NullTelegramNotifier())
    {
        _scheduledTime = scheduledTime;
    }

    public override DateTime ComputeNextRun(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var candidate = today.ToDateTime(_scheduledTime, DateTimeKind.Utc);

        // If the scheduled time hasn't passed today yet, run today
        if (now < candidate)
            return candidate;

        // Otherwise run tomorrow at the same time
        return today.AddDays(1).ToDateTime(_scheduledTime, DateTimeKind.Utc);
    }
}
