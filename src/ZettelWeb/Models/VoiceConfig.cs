namespace ZettelWeb.Models;

/// <summary>User-defined voice style configuration for a content medium.</summary>
public class VoiceConfig
{
    /// <summary>Unique identifier (timestamp-based, 21 characters).</summary>
    public required string Id { get; set; }
    /// <summary>Target medium: "blog", "social", or "all".</summary>
    public required string Medium { get; set; }
    /// <summary>Free-form style notes describing desired voice/tone.</summary>
    public string? StyleNotes { get; set; }
    /// <summary>When this config was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
