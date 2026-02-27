namespace ZettelWeb.Models;

/// <summary>Schedule settings for a single content type.</summary>
public class ScheduleTypeOptions
{
    /// <summary>Whether this content type's scheduled generation is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Day of week for weekly schedules (blog). Ignored for daily schedules.</summary>
    public string DayOfWeek { get; set; } = "Monday";

    /// <summary>Time of day (UTC) to run generation, e.g. "09:00".</summary>
    public string TimeOfDay { get; set; } = "09:00";
}

/// <summary>
/// Per-type schedule configuration under ContentGeneration:Schedule.
/// Blog runs on a weekly cadence; Social runs daily.
/// </summary>
public class ContentScheduleOptions
{
    public const string SectionName = "ContentGeneration:Schedule";

    public ScheduleTypeOptions Blog { get; set; } = new();
    public ScheduleTypeOptions Social { get; set; } = new();
}
