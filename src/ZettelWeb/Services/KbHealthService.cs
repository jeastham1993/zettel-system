using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;
using System.Diagnostics;

namespace ZettelWeb.Services;

public class KbHealthService : IKbHealthService
{
    private const int OrphanWindowDays = 30;
    private const int TopClusterCount = 5;
    private const double SuggestionThreshold = 0.6;

    private readonly ZettelDbContext _db;
    private readonly ILogger<KbHealthService> _logger;

    public KbHealthService(ZettelDbContext db, ILogger<KbHealthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<KbHealthOverview> GetOverviewAsync()
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_overview");

        // Load all permanent notes (content needed for wiki-link parsing).
        var notes = await _db.Notes
            .AsNoTracking()
            .Where(n => n.Status == NoteStatus.Permanent)
            .Select(n => new { n.Id, n.Title, n.CreatedAt, n.EmbedStatus, n.Content })
            .ToListAsync();

        if (notes.Count == 0)
        {
            return new KbHealthOverview(
                new KbHealthScorecard(0, 0, 0, 0),
                Array.Empty<UnconnectedNote>(),
                Array.Empty<ClusterSummary>(),
                Array.Empty<UnusedSeedNote>());
        }

        var idToTitle = notes.ToDictionary(n => n.Id, n => n.Title);

        var titleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in notes)
            titleToId.TryAdd(n.Title, n.Id);

        // Build undirected adjacency from wiki-links
        var adjacency = notes.ToDictionary(n => n.Id, _ => new HashSet<string>());
        foreach (var note in notes)
        {
            foreach (var linkedTitle in WikiLinkParser.ExtractLinkedTitles(note.Content))
            {
                if (titleToId.TryGetValue(linkedTitle, out var targetId) && targetId != note.Id
                    && adjacency.ContainsKey(targetId))
                {
                    adjacency[note.Id].Add(targetId);
                    adjacency[targetId].Add(note.Id);
                }
            }
        }

        // Add semantic edges (pgvector) — skipped gracefully on InMemory
        try
        {
            var semanticEdges = await _db.Database
                .SqlQuery<SemanticEdge>($"""
                    SELECT n1."Id" AS "SourceId",
                           n2."Id" AS "TargetId",
                           (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector))::float8 AS "Similarity"
                    FROM "Notes" n1
                    CROSS JOIN LATERAL (
                        SELECT "Id", "Embedding"
                        FROM "Notes" n2
                        WHERE n2."Id" != n1."Id"
                          AND n2."Embedding" IS NOT NULL
                          AND n2."Status" = 'Permanent'
                        ORDER BY n1."Embedding"::vector <=> n2."Embedding"::vector
                        LIMIT 5
                    ) n2
                    WHERE n1."Embedding" IS NOT NULL
                      AND n1."Status" = 'Permanent'
                      AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > {SuggestionThreshold}
                    """)
                .ToListAsync();

            foreach (var se in semanticEdges)
            {
                if (adjacency.ContainsKey(se.SourceId) && adjacency.ContainsKey(se.TargetId))
                {
                    adjacency[se.SourceId].Add(se.TargetId);
                    adjacency[se.TargetId].Add(se.SourceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Semantic edge query skipped in health overview (likely InMemory provider)");
        }

        var edgeCounts = adjacency.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

        // ── Scorecard ──────────────────────────────────────────────────────
        var totalNotes = notes.Count;
        var embeddedCount = notes.Count(n => n.EmbedStatus == EmbedStatus.Completed);
        var embeddedPercent = (int)Math.Round(embeddedCount * 100.0 / totalNotes);
        var avgConnections = Math.Round(edgeCounts.Values.Average(), 1);

        // ── New & Unconnected (recent orphans) ─────────────────────────────
        var cutoff = DateTime.UtcNow.AddDays(-OrphanWindowDays);
        var orphans = notes
            .Where(n => n.CreatedAt >= cutoff && edgeCounts.GetValueOrDefault(n.Id) == 0)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new UnconnectedNote(
                n.Id,
                n.Title,
                n.CreatedAt,
                n.EmbedStatus == EmbedStatus.Completed ? 5 : 0))
            .ToList();

        // ── Clusters via union-find ────────────────────────────────────────
        var clusters = BuildClusters(notes.Select(n => n.Id).ToList(), adjacency, edgeCounts, idToTitle);

        // ── Never-used seeds ───────────────────────────────────────────────
        var usedSeedIds = await _db.UsedSeedNotes
            .AsNoTracking()
            .Select(u => u.NoteId)
            .ToHashSetAsync();

        var unusedSeeds = notes
            .Where(n => n.EmbedStatus == EmbedStatus.Completed && !usedSeedIds.Contains(n.Id))
            .OrderByDescending(n => edgeCounts.GetValueOrDefault(n.Id))
            .Select(n => new UnusedSeedNote(n.Id, n.Title, edgeCounts.GetValueOrDefault(n.Id)))
            .ToList();

        activity?.SetTag("kb_health.note_count", totalNotes);
        activity?.SetTag("kb_health.orphan_count", orphans.Count);
        activity?.SetTag("kb_health.embedded_percent", embeddedPercent);
        _logger.LogInformation(
            "KB health overview: {NoteCount} notes, {OrphanCount} orphans, {EmbeddedPercent}% embedded",
            totalNotes, orphans.Count, embeddedPercent);

        return new KbHealthOverview(
            new KbHealthScorecard(totalNotes, embeddedPercent, orphans.Count, avgConnections),
            orphans,
            clusters,
            unusedSeeds);
    }

    public async Task<IReadOnlyList<ConnectionSuggestion>> GetConnectionSuggestionsAsync(
        string noteId, int limit = 5)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_suggestions");
        activity?.SetTag("kb_health.note_id", noteId);
        activity?.SetTag("kb_health.limit", limit);

        var hasEmbedding = await _db.Notes
            .AsNoTracking()
            .AnyAsync(n => n.Id == noteId && n.Embedding != null);

        if (!hasEmbedding)
            return Array.Empty<ConnectionSuggestion>();

        try
        {
            return await _db.Database
                .SqlQuery<ConnectionSuggestion>($"""
                    SELECT n2."Id" AS "NoteId",
                           n2."Title",
                           (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector))::float8 AS "Similarity"
                    FROM "Notes" n1
                    CROSS JOIN LATERAL (
                        SELECT "Id", "Title", "Embedding"
                        FROM "Notes" n2
                        WHERE n2."Id" != n1."Id"
                          AND n2."Embedding" IS NOT NULL
                          AND n2."Status" = 'Permanent'
                        ORDER BY n1."Embedding"::vector <=> n2."Embedding"::vector
                        LIMIT {limit}
                    ) n2
                    WHERE n1."Id" = {noteId}
                      AND n1."Embedding" IS NOT NULL
                      AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > {SuggestionThreshold}
                    ORDER BY "Similarity" DESC
                    """)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection suggestion query failed for note {NoteId}", noteId);
            return Array.Empty<ConnectionSuggestion>();
        }
    }

    public async Task<Note?> InsertWikilinkAsync(string orphanNoteId, string targetNoteId)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.insert_wikilink");
        activity?.SetTag("kb_health.orphan_note_id", orphanNoteId);
        activity?.SetTag("kb_health.target_note_id", targetNoteId);

        var orphan = await _db.Notes.FindAsync(orphanNoteId);
        if (orphan is null) return null;

        var target = await _db.Notes.FindAsync(targetNoteId);
        if (target is null) return null;

        orphan.Content += $"<p>[[{target.Title}]]</p>";
        orphan.UpdatedAt = DateTime.UtcNow;
        orphan.EmbedStatus = EmbedStatus.Stale;

        await _db.SaveChangesAsync();

        ZettelTelemetry.WikilinksInserted.Add(1);
        _logger.LogInformation(
            "Wikilink inserted: {OrphanNoteId} -> {TargetNoteId} ({TargetTitle})",
            orphanNoteId, targetNoteId, target.Title);

        return orphan;
    }

    /// <summary>
    /// Union-find on the adjacency map to identify connected components.
    /// Returns top <see cref="TopClusterCount"/> components (by size, minimum 2 members),
    /// each labelled by the note with the highest edge count (the hub).
    /// </summary>
    private static List<ClusterSummary> BuildClusters(
        List<string> noteIds,
        Dictionary<string, HashSet<string>> adjacency,
        Dictionary<string, int> edgeCounts,
        Dictionary<string, string> idToTitle)
    {
        var parent = noteIds.ToDictionary(id => id, id => id);

        string Find(string id)
        {
            if (parent[id] != id)
                parent[id] = Find(parent[id]);
            return parent[id];
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        foreach (var (nodeId, neighbors) in adjacency)
        {
            foreach (var neighbor in neighbors)
                Union(nodeId, neighbor);
        }

        return noteIds
            .GroupBy(Find)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .Take(TopClusterCount)
            .Select(g =>
            {
                var members = g.ToList();
                var hubId = members.OrderByDescending(id => edgeCounts.GetValueOrDefault(id)).First();
                var hubTitle = idToTitle.GetValueOrDefault(hubId, hubId);
                return new ClusterSummary(hubId, hubTitle, members.Count);
            })
            .ToList();
    }
}
