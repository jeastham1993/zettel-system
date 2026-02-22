namespace ZettelWeb.Models;

/// <summary>A user-provided writing sample used to guide voice/tone in generated content.</summary>
public class VoiceExample
{
    /// <summary>Unique identifier (timestamp-based, 21 characters).</summary>
    public required string Id { get; set; }
    /// <summary>Target medium: "blog", "social", or "all".</summary>
    public required string Medium { get; set; }
    /// <summary>Optional title of the writing sample.</summary>
    public string? Title { get; set; }
    /// <summary>The writing sample content.</summary>
    public required string Content { get; set; }
    /// <summary>Optional source attribution for the sample.</summary>
    public string? Source { get; set; }
    /// <summary>When this example was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
