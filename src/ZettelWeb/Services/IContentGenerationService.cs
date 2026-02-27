using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>Generates blog and social media content from a topic cluster using an LLM.</summary>
public interface IContentGenerationService
{
    /// <summary>
    /// Generate content pieces from a topic cluster.
    /// Persists the generation and pieces to the database.
    /// </summary>
    /// <param name="cluster">The topic cluster to generate from.</param>
    /// <param name="mediums">
    /// Optional list of mediums to generate (e.g. ["blog"], ["social"]).
    /// Pass null to generate all mediums (default behaviour).
    /// </param>
    Task<ContentGeneration> GenerateContentAsync(
        TopicCluster cluster,
        IReadOnlyList<string>? mediums = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerate content pieces for a specific medium on an existing generation.
    /// Replaces Draft pieces for that medium; Approved pieces are never touched.
    /// Persists changes to the database.
    /// </summary>
    Task<List<ContentPiece>> RegenerateMediumAsync(
        ContentGeneration generation,
        IReadOnlyList<Note> notes,
        string medium,
        CancellationToken cancellationToken = default);
}
