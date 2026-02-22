using System.Text.Json.Serialization;

namespace ZettelWeb.Models;

/// <summary>Embedding processing status using the outbox pattern.</summary>
public enum EmbedStatus
{
    /// <summary>Note is queued for embedding generation.</summary>
    Pending,
    /// <summary>Embedding is currently being generated.</summary>
    Processing,
    /// <summary>Embedding has been successfully generated.</summary>
    Completed,
    /// <summary>Embedding generation failed after retries.</summary>
    Failed,
    /// <summary>Content changed since last embedding; needs re-processing.</summary>
    Stale
}

/// <summary>Whether a note is permanent or a fleeting inbox capture.</summary>
public enum NoteStatus
{
    /// <summary>A permanent, indexed note in the Zettelkasten.</summary>
    Permanent = 0,
    /// <summary>A fleeting note in the inbox awaiting review.</summary>
    Fleeting = 1
}

/// <summary>URL enrichment processing status.</summary>
public enum EnrichStatus
{
    /// <summary>No enrichment needed.</summary>
    None = 0,
    /// <summary>Queued for URL metadata extraction.</summary>
    Pending = 1,
    /// <summary>Enrichment completed successfully.</summary>
    Completed = 2,
    /// <summary>Enrichment failed after retries.</summary>
    Failed = 3,
    /// <summary>Enrichment is currently being processed.</summary>
    Processing = 4
}

/// <summary>The type of note in the Zettelkasten system.</summary>
public enum NoteType
{
    /// <summary>A standard note with content.</summary>
    Regular = 0,
    /// <summary>A structure note that organizes other notes.</summary>
    Structure = 1,
    /// <summary>A source note with bibliographic metadata.</summary>
    Source = 2
}

/// <summary>A note in the Zettelkasten knowledge management system.</summary>
public class Note
{
    /// <summary>Unique identifier (timestamp-based, 17 characters).</summary>
    public required string Id { get; set; }
    /// <summary>The title of the note.</summary>
    public required string Title { get; set; }
    /// <summary>The HTML content of the note.</summary>
    public required string Content { get; set; }
    /// <summary>When the note was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the note was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether this is a permanent or fleeting (inbox) note.</summary>
    public NoteStatus Status { get; set; } = NoteStatus.Permanent;
    /// <summary>Capture source: "web", "email", or "telegram".</summary>
    public string? Source { get; set; }
    [JsonIgnore]
    public string? EnrichmentJson { get; set; }
    /// <summary>URL enrichment processing status.</summary>
    public EnrichStatus EnrichStatus { get; set; } = EnrichStatus.None;
    [JsonIgnore]
    public int EnrichRetryCount { get; set; }

    // Embedding state (outbox pattern)
    // NOTE: Must remain float[] (not Pgvector.Vector) for InMemory test compatibility.
    // The SearchService converts to Vector when passing to raw SQL queries.
    [JsonIgnore]
    public float[]? Embedding { get; set; }
    /// <summary>Embedding generation processing status.</summary>
    public EmbedStatus EmbedStatus { get; set; } = EmbedStatus.Pending;
    [JsonIgnore]
    public string? EmbeddingModel { get; set; }
    [JsonIgnore]
    public string? EmbedError { get; set; }
    [JsonIgnore]
    public int EmbedRetryCount { get; set; }
    [JsonIgnore]
    public DateTime? EmbedUpdatedAt { get; set; }

    /// <summary>The type of note: Regular, Structure, or Source.</summary>
    public NoteType NoteType { get; set; } = NoteType.Regular;

    // Source metadata (only populated when NoteType = Source)
    /// <summary>Author of the source material.</summary>
    public string? SourceAuthor { get; set; }
    /// <summary>Title of the source material.</summary>
    public string? SourceTitle { get; set; }
    /// <summary>URL of the source material.</summary>
    public string? SourceUrl { get; set; }
    /// <summary>Publication year of the source material.</summary>
    public int? SourceYear { get; set; }
    /// <summary>Type of source: "book", "article", "web", "podcast", or "other".</summary>
    public string? SourceType { get; set; }

    /// <summary>Tags associated with this note.</summary>
    public List<NoteTag> Tags { get; set; } = new();

    // Version history
    [JsonIgnore]
    public List<NoteVersion> Versions { get; set; } = new();
}

/// <summary>A snapshot of a note at a point in time.</summary>
public class NoteVersion
{
    /// <summary>Auto-incremented version ID.</summary>
    public int Id { get; set; }
    /// <summary>The ID of the parent note.</summary>
    public required string NoteId { get; set; }
    /// <summary>The title at the time of this version.</summary>
    public required string Title { get; set; }
    /// <summary>The content at the time of this version.</summary>
    public required string Content { get; set; }
    /// <summary>Comma-separated tags at the time of this version.</summary>
    public string? Tags { get; set; }
    /// <summary>When this version was saved (UTC).</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A note that links back to the queried note via [[wiki-links]].</summary>
public record BacklinkResult(string Id, string Title);

/// <summary>Result of a duplicate content check.</summary>
/// <param name="IsDuplicate">Whether a duplicate was found above the similarity threshold.</param>
/// <param name="SimilarNoteId">The ID of the most similar existing note, if any.</param>
/// <param name="SimilarNoteTitle">The title of the most similar existing note, if any.</param>
/// <param name="Similarity">Cosine similarity score (0.0–1.0).</param>
public record DuplicateCheckResult(
    bool IsDuplicate,
    string? SimilarNoteId,
    string? SimilarNoteTitle,
    double Similarity);
