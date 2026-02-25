using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class KbHealthService : IKbHealthService
{
    private const int OrphanWindowDays = 30;
    private const int TopClusterCount = 5;

    private readonly ZettelDbContext _db;
    private readonly IChatClient _chatClient;
    private readonly ILogger<KbHealthService> _logger;
    private readonly int _largeNoteThreshold;
    private readonly int _summarizeTargetLength;
    private readonly double _suggestionThreshold;

    public KbHealthService(
        ZettelDbContext db,
        IChatClient chatClient,
        IConfiguration configuration,
        ILogger<KbHealthService> logger)
    {
        _db = db;
        _chatClient = chatClient;
        _logger = logger;
        _largeNoteThreshold = configuration.GetValue("Embedding:MaxInputCharacters", 4_000);
        _summarizeTargetLength = (int)(_largeNoteThreshold * 0.8);
        // Default 0.3: works across embedding models with compressed score distributions
        // (e.g. Titan Embeddings). Raise toward 0.6 if nomic-embed-text is in use.
        _suggestionThreshold = configuration.GetValue("Search:_suggestionThreshold", 0.3);
    }

    public async Task<KbHealthOverview> GetOverviewAsync()
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_overview");
        var sw = Stopwatch.StartNew();

        // Load all permanent notes (content needed for wiki-link parsing).
        var notes = await _db.Notes
            .AsNoTracking()
            .Where(n => n.Status == NoteStatus.Permanent)
            .Select(n => new { n.Id, n.Title, n.CreatedAt, n.EmbedStatus, n.Content })
            .ToListAsync();

        if (notes.Count == 0)
        {
            sw.Stop();
            ZettelTelemetry.KbHealthOverviewDuration.Record(sw.Elapsed.TotalMilliseconds);
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
                      AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > {_suggestionThreshold}
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

        sw.Stop();
        ZettelTelemetry.KbHealthOverviewDuration.Record(sw.Elapsed.TotalMilliseconds);
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
                      AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > {_suggestionThreshold}
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

        _db.NoteVersions.Add(new NoteVersion
        {
            NoteId = orphan.Id,
            Title = orphan.Title,
            Content = orphan.Content,
            SavedAt = DateTime.UtcNow
        });

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

    public async Task<IReadOnlyList<UnembeddedNote>> GetNotesWithoutEmbeddingsAsync()
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_missing_embeddings");

        return await _db.Notes
            .AsNoTracking()
            .Where(n => n.Status == NoteStatus.Permanent && n.EmbedStatus != EmbedStatus.Completed)
            .OrderBy(n => n.EmbedStatus)
            .ThenByDescending(n => n.CreatedAt)
            .Select(n => new UnembeddedNote(n.Id, n.Title, n.CreatedAt, n.EmbedStatus, n.EmbedError))
            .ToListAsync();
    }

    public async Task<int> RequeueEmbeddingAsync(string noteId)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.requeue_embedding");
        activity?.SetTag("kb_health.note_id", noteId);

        var note = await _db.Notes.FindAsync(noteId);
        if (note is null) return 0;

        note.EmbedStatus = EmbedStatus.Pending;
        note.EmbedError = null;
        note.EmbedRetryCount = 0;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Embedding requeued for note {NoteId}", noteId);
        return 1;
    }

    public async Task<IReadOnlyList<LargeNote>> GetLargeNotesAsync()
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_large_notes");

        var notes = await _db.Notes
            .AsNoTracking()
            .Where(n => n.Status == NoteStatus.Permanent && n.Content.Length > _largeNoteThreshold)
            .OrderByDescending(n => n.Content.Length)
            .Select(n => new LargeNote(n.Id, n.Title, n.UpdatedAt, n.Content.Length))
            .ToListAsync();

        activity?.SetTag("kb_health.large_note_count", notes.Count);
        _logger.LogInformation("Found {Count} large notes above {Threshold} chars", notes.Count, _largeNoteThreshold);

        return notes;
    }

    public async Task<SummarizeNoteResponse?> SummarizeNoteAsync(
        string noteId, CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.summarize_note");
        activity?.SetTag("kb_health.note_id", noteId);

        var note = await _db.Notes.FindAsync(new object[] { noteId }, cancellationToken);
        if (note is null) return null;

        var originalLength = note.Content.Length;

        _db.NoteVersions.Add(new NoteVersion
        {
            NoteId = note.Id,
            Title = note.Title,
            Content = note.Content,
            SavedAt = DateTime.UtcNow
        });

        var prompt = $"""
            You are condensing a note for a personal knowledge base.
            Summarize the following note, preserving all key ideas and insights.
            Target length: under {_summarizeTargetLength} characters.
            Output ONLY the summarized content — no preamble or explanation.

            --- NOTE: {note.Title} ---
            {note.Content}
            """;

        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { MaxOutputTokens = 1000, Temperature = 0.2f },
            cancellationToken);

        var summary = response.Text?.Trim() ?? note.Content;

        note.Content = summary;
        note.EmbedStatus = EmbedStatus.Stale;
        note.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var summarizedLength = summary.Length;
        var stillLarge = summarizedLength > _largeNoteThreshold;

        if (stillLarge)
            _logger.LogWarning(
                "Summarized note {NoteId} still exceeds threshold ({Length} > {Threshold})",
                noteId, summarizedLength, _largeNoteThreshold);
        else
            _logger.LogInformation(
                "Summarized note {NoteId}: {Original} → {Summarized} chars",
                noteId, originalLength, summarizedLength);

        activity?.SetTag("kb_health.original_length", originalLength);
        activity?.SetTag("kb_health.summarized_length", summarizedLength);
        activity?.SetTag("kb_health.still_large", stillLarge);

        return new SummarizeNoteResponse(noteId, originalLength, summarizedLength, stillLarge);
    }

    public async Task<SplitSuggestion?> GetSplitSuggestionsAsync(
        string noteId, CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.get_split_suggestions");
        activity?.SetTag("kb_health.note_id", noteId);

        var note = await _db.Notes.FindAsync(new object[] { noteId }, cancellationToken);
        if (note is null) return null;

        var prompt = $$"""
            You are a Zettelkasten assistant. The following note is too large and contains multiple ideas.
            Break it down into 2-5 atomic notes, each focusing on one distinct concept or idea.
            Return ONLY valid JSON with no preamble, explanation, or markdown fencing.

            Required JSON format:
            {"notes":[{"title":"...", "content":"..."}]}

            --- NOTE: {{note.Title}} ---
            {{note.Content}}
            """;

        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { MaxOutputTokens = 2000, Temperature = 0.3f },
            cancellationToken);

        var raw = response.Text?.Trim() ?? "{}";
        var json = StripMarkdownCodeFences(raw);

        try
        {
            var parsed = JsonSerializer.Deserialize<SplitSuggestionJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var notes = (parsed?.Notes ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n.Title) && !string.IsNullOrWhiteSpace(n.Content))
                .Select(n => new SuggestedNote(n.Title!.Trim(), n.Content!.Trim()))
                .ToList();

            _logger.LogInformation(
                "Split suggestions for note {NoteId}: {Count} atomic notes suggested",
                noteId, notes.Count);

            activity?.SetTag("kb_health.suggestion_count", notes.Count);
            return new SplitSuggestion(noteId, note.Title, notes);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse split suggestion JSON for note {NoteId}", noteId);
            return new SplitSuggestion(noteId, note.Title, []);
        }
    }

    public async Task<ApplySplitResponse?> ApplySplitAsync(
        string noteId, IReadOnlyList<SuggestedNote> notes, CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("kb_health.apply_split");
        activity?.SetTag("kb_health.note_id", noteId);
        activity?.SetTag("kb_health.note_count", notes.Count);

        var original = await _db.Notes.FindAsync(new object[] { noteId }, cancellationToken);
        if (original is null) return null;

        var createdIds = new List<string>(notes.Count);

        foreach (var suggested in notes)
        {
            var newId = GenerateId();
            _db.Notes.Add(new Note
            {
                Id = newId,
                Title = suggested.Title,
                Content = suggested.Content,
                Status = NoteStatus.Permanent,
                EmbedStatus = EmbedStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            createdIds.Add(newId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Applied split for note {OriginalNoteId}: created {Count} new notes",
            noteId, createdIds.Count);

        ZettelTelemetry.NoteSplitsApplied.Add(1);
        return new ApplySplitResponse(noteId, createdIds);
    }

    private static string StripMarkdownCodeFences(string text)
    {
        // Strip ```json ... ``` or ``` ... ``` wrappers that LLMs commonly add
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```");
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    private static string GenerateId()
    {
        var now = DateTime.UtcNow;
        return $"{now:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 9999)}";
    }

    // ── Internal JSON shape for LLM response parsing ─────────────────────────

    private sealed class SplitSuggestionJson
    {
        public List<SuggestedNoteJson>? Notes { get; set; }
    }

    private sealed class SuggestedNoteJson
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
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
