using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ZettelWeb.Background;

/// <summary>
/// Scheduled background service that generates blog posts on a weekly cadence.
/// Reads schedule from ContentGeneration:Schedule:Blog (DayOfWeek + TimeOfDay).
/// </summary>
public class BlogContentScheduler : ContentSchedulerBase
{
    private readonly DayOfWeek _scheduledDay;
    private readonly TimeOnly _scheduledTime;

    protected override IReadOnlyList<string> Mediums => ["blog"];

    public BlogContentScheduler(
        IServiceProvider serviceProvider,
        ILogger<BlogContentScheduler> logger,
        IConfiguration configuration) : base(serviceProvider, logger)
    {
        var dayStr = configuration["ContentGeneration:Schedule:Blog:DayOfWeek"] ?? "Monday";
        _scheduledDay = Enum.TryParse<DayOfWeek>(dayStr, ignoreCase: true, out var day)
            ? day
            : DayOfWeek.Monday;

        var timeStr = configuration["ContentGeneration:Schedule:Blog:TimeOfDay"] ?? "09:00";
        _scheduledTime = TimeOnly.TryParse(timeStr, out var time)
            ? time
            : new TimeOnly(9, 0);
    }

    /// <summary>Constructor for unit testing — accepts schedule values directly.</summary>
    public BlogContentScheduler(DayOfWeek scheduledDay, TimeOnly scheduledTime)
        : base(serviceProvider: null!, logger: null!)
    {
        _scheduledDay = scheduledDay;
        _scheduledTime = scheduledTime;
    }

    public override DateTime ComputeNextRun(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var candidate = today.ToDateTime(_scheduledTime, DateTimeKind.Utc);

        // If today is the scheduled day and we haven't reached the time yet, run today
        if (today.DayOfWeek == _scheduledDay && now < candidate)
            return candidate;

        // Otherwise find the next occurrence of the scheduled day
        var daysUntilTarget = (int)_scheduledDay - (int)today.DayOfWeek;
        if (daysUntilTarget <= 0)
            daysUntilTarget += 7;

        var nextDate = today.AddDays(daysUntilTarget);
        return nextDate.ToDateTime(_scheduledTime, DateTimeKind.Utc);
    }
}
