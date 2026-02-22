namespace ZettelWeb.Models;

/// <summary>Tracks notes that have been used as generation seeds to prevent reuse.</summary>
public class UsedSeedNote
{
    /// <summary>The note ID that was used as a seed (also the primary key).</summary>
    public required string NoteId { get; set; }
    /// <summary>When this note was used as a seed (UTC).</summary>
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
}
