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

/// <summary>A permanent note whose content exceeds the embedding character limit.</summary>
public record LargeNote(
    string Id,
    string Title,
    DateTime UpdatedAt,
    int CharacterCount);

/// <summary>Response returned after an LLM summarization of a large note.</summary>
public record SummarizeNoteResponse(
    string NoteId,
    int OriginalLength,
    int SummarizedLength,
    bool StillLarge);

/// <summary>A single atomic note suggested by the LLM when splitting a large note.</summary>
public record SuggestedNote(string Title, string Content);

/// <summary>LLM-generated split suggestions for a large note. No changes are made until ApplySplit is called.</summary>
public record SplitSuggestion(
    string NoteId,
    string OriginalTitle,
    IReadOnlyList<SuggestedNote> Notes);

/// <summary>Request body to confirm and apply a note split.</summary>
public record ApplySplitRequest(IReadOnlyList<SuggestedNote> Notes);

/// <summary>Response after applying a note split. The original note is preserved untouched.</summary>
public record ApplySplitResponse(
    string OriginalNoteId,
    IReadOnlyList<string> CreatedNoteIds);
