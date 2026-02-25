using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ZettelWeb;

public static class ZettelTelemetry
{
    public const string ServiceName = "ZettelWeb";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    // ── Metrics ──────────────────────────────────────────────
    public static readonly Counter<long> NotesCreated =
        Meter.CreateCounter<long>("zettel.notes.created", description: "Number of notes created");

    public static readonly Counter<long> NotesDeleted =
        Meter.CreateCounter<long>("zettel.notes.deleted", description: "Number of notes deleted");

    public static readonly Counter<long> SearchesExecuted =
        Meter.CreateCounter<long>("zettel.searches.executed", description: "Number of searches executed");

    public static readonly Counter<long> EmbeddingsProcessed =
        Meter.CreateCounter<long>("zettel.embeddings.processed", description: "Number of embeddings processed");

    public static readonly Counter<long> EmbeddingsFailed =
        Meter.CreateCounter<long>("zettel.embeddings.failed", description: "Number of embedding failures");

    public static readonly Counter<long> EnrichmentsProcessed =
        Meter.CreateCounter<long>("zettel.enrichments.processed", description: "Number of enrichments processed");

    public static readonly Counter<long> EnrichmentsFailed =
        Meter.CreateCounter<long>("zettel.enrichments.failed", description: "Number of enrichment failures");

    public static readonly Histogram<double> EmbeddingDuration =
        Meter.CreateHistogram<double>("zettel.embeddings.duration", "ms",
            "Time taken to generate an embedding");

    public static readonly Histogram<double> SearchDuration =
        Meter.CreateHistogram<double>("zettel.searches.duration", "ms",
            "Time taken to execute a search");

    public static readonly Counter<long> ContentGenerated =
        Meter.CreateCounter<long>("zettel.content.generated", description: "Number of content generation runs");

    public static readonly Counter<long> ScheduledGenerations =
        Meter.CreateCounter<long>("zettel.content.scheduled_generations",
            description: "Number of scheduled content generation runs");

    public static readonly Counter<long> DraftsSent =
        Meter.CreateCounter<long>("zettel.content.drafts_sent", description: "Number of pieces successfully sent to draft.");

    public static readonly Counter<long> DraftSendFailures =
        Meter.CreateCounter<long>("zettel.content.draft_send_failures", description: "Number of failed draft send attempts.");

    public static readonly Counter<long> EditorFeedbackGenerated =
        Meter.CreateCounter<long>("zettel.content.editor_feedback_generated", description: "Number of editor feedback passes completed.");

    public static readonly Histogram<double> PublishingDurationMs =
        Meter.CreateHistogram<double>("zettel.content.publishing_duration", "ms", "Duration of external publish calls in milliseconds.");

    public static readonly Counter<long> WikilinksInserted =
        Meter.CreateCounter<long>("zettel.kb_health.wikilinks_inserted", description: "Number of wikilinks inserted via the KB health dashboard.");

    public static readonly Histogram<double> KbHealthOverviewDuration =
        Meter.CreateHistogram<double>("zettel.kb_health.overview_duration", "ms",
            "Time to compute the full KB health overview (note load, wiki-link parse, pgvector edges, union-find).");

    public static readonly Counter<long> NoteSplitsApplied =
        Meter.CreateCounter<long>("zettel.kb_health.note_splits_applied", description: "Number of large note splits applied.");
}
