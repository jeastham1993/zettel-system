using ZettelWeb.Background;

namespace ZettelWeb.Tests.Background;

/// <summary>
/// Unit tests for the ComputeNextRun scheduling logic in both weekly (blog)
/// and daily (social) schedulers. These tests are pure date math — no I/O.
/// </summary>
public class ContentSchedulerTests
{
    // ── BlogContentScheduler (weekly) ──────────────────────────────────────

    [Fact]
    public void BlogScheduler_WhenTodayIsScheduledDayAndBeforeTime_ReturnsTodayAtScheduledTime()
    {
        // Monday 08:00 UTC, scheduled for Monday 09:00
        var now = new DateTime(2026, 2, 23, 8, 0, 0, DateTimeKind.Utc); // Monday
        var scheduler = new BlogContentScheduler(DayOfWeek.Monday, new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 2, 23, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void BlogScheduler_WhenTodayIsScheduledDayButAfterTime_ReturnsNextWeek()
    {
        // Monday 10:00 UTC, scheduled for Monday 09:00 (already past)
        var now = new DateTime(2026, 2, 23, 10, 0, 0, DateTimeKind.Utc); // Monday
        var scheduler = new BlogContentScheduler(DayOfWeek.Monday, new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void BlogScheduler_WhenDifferentDay_ReturnsNextOccurrenceOfScheduledDay()
    {
        // Wednesday 12:00 UTC, scheduled for Monday 09:00
        var now = new DateTime(2026, 2, 25, 12, 0, 0, DateTimeKind.Utc); // Wednesday
        var scheduler = new BlogContentScheduler(DayOfWeek.Monday, new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        // Next Monday from Wednesday is +5 days
        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), next);
    }

    // ── SocialContentScheduler (daily) ─────────────────────────────────────

    [Fact]
    public void SocialScheduler_WhenBeforeScheduledTimeToday_ReturnsTodayAtScheduledTime()
    {
        // 08:00 UTC, scheduled for 09:00
        var now = new DateTime(2026, 2, 23, 8, 0, 0, DateTimeKind.Utc);
        var scheduler = new SocialContentScheduler(new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 2, 23, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void SocialScheduler_WhenAfterScheduledTimeToday_ReturnsTomorrowAtScheduledTime()
    {
        // 10:00 UTC, scheduled for 09:00 (already past today)
        var now = new DateTime(2026, 2, 23, 10, 0, 0, DateTimeKind.Utc);
        var scheduler = new SocialContentScheduler(new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 2, 24, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void SocialScheduler_WhenExactlyAtScheduledTime_ReturnsTomorrowAtScheduledTime()
    {
        // Exactly 09:00 UTC — the moment has passed (not strictly before), so next run is tomorrow
        var now = new DateTime(2026, 2, 23, 9, 0, 0, DateTimeKind.Utc);
        var scheduler = new SocialContentScheduler(new TimeOnly(9, 0));

        var next = scheduler.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 2, 24, 9, 0, 0, DateTimeKind.Utc), next);
    }
}
