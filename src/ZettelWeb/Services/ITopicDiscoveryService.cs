using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>A cluster of related notes discovered from a seed note.</summary>
public record TopicCluster(
    string SeedNoteId,
    IReadOnlyList<Note> Notes,
    string TopicSummary);

/// <summary>Discovers topic clusters by selecting random seed notes and traversing the knowledge graph.</summary>
public interface ITopicDiscoveryService
{
    Task<TopicCluster?> DiscoverTopicAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a topic cluster starting from a specific seed note.
    /// Returns null if the note does not exist or is not a Permanent note.
    /// </summary>
    Task<TopicCluster?> DiscoverTopicAsync(string seedNoteId, CancellationToken cancellationToken = default);
}
