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
}
