using System.Text.Json.Serialization;

namespace ZettelWeb.Models;

public enum EmbedStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Stale
}

public enum NoteStatus
{
    Permanent = 0,
    Fleeting = 1
}

public enum EnrichStatus
{
    None = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Processing = 4
}

public enum NoteType
{
    Regular = 0,
    Structure = 1,
    Source = 2
}

public class Note
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Fleeting notes status
    public NoteStatus Status { get; set; } = NoteStatus.Permanent;
    public string? Source { get; set; }  // "web", "email", "telegram"
    [JsonIgnore]
    public string? EnrichmentJson { get; set; }
    public EnrichStatus EnrichStatus { get; set; } = EnrichStatus.None;
    [JsonIgnore]
    public int EnrichRetryCount { get; set; }

    // Embedding state (outbox pattern)
    // NOTE: Must remain float[] (not Pgvector.Vector) for InMemory test compatibility.
    // The SearchService converts to Vector when passing to raw SQL queries.
    [JsonIgnore]
    public float[]? Embedding { get; set; }
    public EmbedStatus EmbedStatus { get; set; } = EmbedStatus.Pending;
    [JsonIgnore]
    public string? EmbeddingModel { get; set; }
    [JsonIgnore]
    public string? EmbedError { get; set; }
    [JsonIgnore]
    public int EmbedRetryCount { get; set; }
    [JsonIgnore]
    public DateTime? EmbedUpdatedAt { get; set; }

    // Note type (only meaningful for Permanent notes)
    public NoteType NoteType { get; set; } = NoteType.Regular;

    // Source metadata (only populated when NoteType = Source)
    public string? SourceAuthor { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public int? SourceYear { get; set; }
    public string? SourceType { get; set; } // "book"|"article"|"web"|"podcast"|"other"

    // Tags
    public List<NoteTag> Tags { get; set; } = new();

    // Version history
    [JsonIgnore]
    public List<NoteVersion> Versions { get; set; } = new();
}

public class NoteVersion
{
    public int Id { get; set; }
    public required string NoteId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public string? Tags { get; set; }  // Comma-separated
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

public record BacklinkResult(string Id, string Title);

public record DuplicateCheckResult(
    bool IsDuplicate,
    string? SimilarNoteId,
    string? SimilarNoteTitle,
    double Similarity);
