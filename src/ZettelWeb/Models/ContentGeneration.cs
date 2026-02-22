using System.Text.Json.Serialization;

namespace ZettelWeb.Models;

/// <summary>Status of a content generation pipeline run.</summary>
public enum GenerationStatus
{
    /// <summary>Generation is queued or in progress.</summary>
    Pending,
    /// <summary>Content has been generated and awaits review.</summary>
    Generated,
    /// <summary>All content pieces have been approved.</summary>
    Approved,
    /// <summary>The generation was rejected by the user.</summary>
    Rejected
}

/// <summary>Represents a single content generation pipeline run seeded from a note cluster.</summary>
public class ContentGeneration
{
    /// <summary>Unique identifier (timestamp-based, 21 characters).</summary>
    public required string Id { get; set; }
    /// <summary>The primary seed note that initiated this generation.</summary>
    public required string SeedNoteId { get; set; }
    /// <summary>IDs of notes in the topic cluster (JSON array in database).</summary>
    public List<string> ClusterNoteIds { get; set; } = new();
    /// <summary>LLM-generated summary of the topic cluster.</summary>
    public required string TopicSummary { get; set; }
    /// <summary>Embedding vector of the topic summary for similarity search.</summary>
    [JsonIgnore]
    public float[]? TopicEmbedding { get; set; }
    /// <summary>Current pipeline status.</summary>
    public GenerationStatus Status { get; set; } = GenerationStatus.Pending;
    /// <summary>When the content was generated (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the content was reviewed (UTC), if applicable.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Generated content pieces for this run.</summary>
    public List<ContentPiece> Pieces { get; set; } = new();
}
