using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>Generates blog and social media content from a topic cluster using an LLM.</summary>
public interface IContentGenerationService
{
    /// <summary>
    /// Generate blog and social content pieces from a topic cluster.
    /// Persists the generation and pieces to the database.
    /// </summary>
    Task<ContentGeneration> GenerateContentAsync(
        TopicCluster cluster,
        CancellationToken cancellationToken = default);
}
