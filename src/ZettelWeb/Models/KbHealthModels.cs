namespace ZettelWeb.Models;

/// <summary>Top-level scorecard metrics for the knowledge base.</summary>
public record KbHealthScorecard(
    int TotalNotes,
    int EmbeddedPercent,
    int OrphanCount,
    double AverageConnections);

/// <summary>A permanent note added in the last 30 days with no connections.</summary>
public record UnconnectedNote(
    string Id,
    string Title,
    DateTime CreatedAt,
    int SuggestionCount);

/// <summary>A semantic cluster represented by its most-connected hub note.</summary>
public record ClusterSummary(
    string HubNoteId,
    string HubTitle,
    int NoteCount);

/// <summary>A permanent, embedded note that has never been used as a generation seed.</summary>
public record UnusedSeedNote(
    string Id,
    string Title,
    int ConnectionCount);

/// <summary>Full KB health overview returned by the overview endpoint.</summary>
public record KbHealthOverview(
    KbHealthScorecard Scorecard,
    IReadOnlyList<UnconnectedNote> NewAndUnconnected,
    IReadOnlyList<ClusterSummary> RichestClusters,
    IReadOnlyList<UnusedSeedNote> NeverUsedAsSeeds);

/// <summary>A semantically similar note suggested as a connection for an orphan.</summary>
public record ConnectionSuggestion(
    string NoteId,
    string Title,
    double Similarity);

/// <summary>Request body for inserting a wikilink into an orphan note.</summary>
public record AddLinkRequest(string TargetNoteId);

/// <summary>A permanent note that does not yet have a completed embedding.</summary>
public record UnembeddedNote(
    string Id,
    string Title,
    DateTime CreatedAt,
    EmbedStatus EmbedStatus,
    string? EmbedError);
