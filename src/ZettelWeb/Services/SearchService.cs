using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class SearchService : ISearchService
{
    private readonly ZettelDbContext _db;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly SearchWeights _weights;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        ZettelDbContext db,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        SearchWeights weights,
        ILogger<SearchService> logger)
    {
        _db = db;
        _embeddingGenerator = embeddingGenerator;
        _weights = weights;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        var results = await _db.Database
            .SqlQuery<SearchResult>($"""
                SELECT "Id" AS "NoteId",
                       "Title",
                       ts_headline('english', "Content",
                                   plainto_tsquery('english', {query}),
                                   'MaxWords=35,MinWords=15,StartSel=,StopSel=') AS "Snippet",
                       ts_rank(to_tsvector('english', "Title" || ' ' || "Content"),
                               plainto_tsquery('english', {query}))::float8 AS "Rank"
                FROM "Notes"
                WHERE to_tsvector('english', "Title" || ' ' || "Content")
                      @@ plainto_tsquery('english', {query})
                ORDER BY "Rank" DESC
                LIMIT 50
                """)
            .ToListAsync();

        return results;
    }

    public async Task<IReadOnlyList<SearchResult>> SemanticSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        var queryVector = await _embeddingGenerator.GenerateVectorAsync(query);
        var queryParam = new Vector(queryVector.ToArray());
        var minSimilarity = _weights.MinimumSimilarity;

        var results = await _db.Database
            .SqlQuery<SearchResult>($"""
                SELECT "Id" AS "NoteId",
                       "Title",
                       CASE WHEN LENGTH("Content") > 200
                            THEN LEFT("Content", 200) || '...'
                            ELSE "Content"
                       END AS "Snippet",
                       (1.0 - ("Embedding"::vector <=> {queryParam}))::float8 AS "Rank"
                FROM "Notes"
                WHERE "Embedding" IS NOT NULL
                  AND 1.0 - ("Embedding"::vector <=> {queryParam}) >= {minSimilarity}
                ORDER BY "Embedding"::vector <=> {queryParam}
                LIMIT 20
                """)
            .ToListAsync();

        return results;
    }

    public async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        // Run sequentially — EF Core DbContext is NOT thread-safe.
        var fullTextResults = await FullTextSearchAsync(query);

        IReadOnlyList<SearchResult> semanticResults;
        try
        {
            semanticResults = await SemanticSearchAsync(query);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search failed, falling back to full-text only");
            return fullTextResults;
        }

        // Normalize full-text ranks to [0, 1]. Semantic scores are already
        // cosine similarity in [0, 1] so they don't need normalization.
        var normalizedFullText = Normalize(fullTextResults);

        // Merge by NoteId, combining weighted scores
        var merged = new Dictionary<string, (SearchResult Result, double Score)>();

        foreach (var r in normalizedFullText)
        {
            merged[r.NoteId] = (r, _weights.FullTextWeight * r.Rank);
        }

        foreach (var r in semanticResults)
        {
            if (merged.TryGetValue(r.NoteId, out var existing))
            {
                merged[r.NoteId] = (existing.Result, existing.Score + _weights.SemanticWeight * r.Rank);
            }
            else
            {
                merged[r.NoteId] = (r, _weights.SemanticWeight * r.Rank);
            }
        }

        var maxScore = merged.Values.Count > 0
            ? merged.Values.Max(v => v.Score)
            : 1.0;

        return merged.Values
            .Where(v => v.Score >= _weights.MinimumHybridScore)
            .Select(v => new SearchResult
            {
                NoteId = v.Result.NoteId,
                Title = v.Result.Title,
                Snippet = v.Result.Snippet,
                Rank = maxScore > 0 ? Math.Min(v.Score / maxScore, 1.0) : 0,
            })
            .OrderByDescending(r => r.Rank)
            .ToList();
    }

    public async Task<IReadOnlyList<SearchResult>> FindRelatedAsync(
        string noteId, int limit = 5)
    {
        var sourceNote = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId);
        if (sourceNote?.Embedding is null)
            return Array.Empty<SearchResult>();

        var sourceVector = new Vector(sourceNote.Embedding);
        var minSimilarity = _weights.MinimumSimilarity;

        var results = await _db.Database
            .SqlQuery<SearchResult>($"""
                SELECT "Id" AS "NoteId",
                       "Title",
                       CASE WHEN LENGTH("Content") > 200
                            THEN LEFT("Content", 200) || '...'
                            ELSE "Content"
                       END AS "Snippet",
                       (1.0 - ("Embedding"::vector <=> {sourceVector}))::float8 AS "Rank"
                FROM "Notes"
                WHERE "Embedding" IS NOT NULL
                  AND "Id" != {noteId}
                  AND 1.0 - ("Embedding"::vector <=> {sourceVector}) >= {minSimilarity}
                ORDER BY "Embedding"::vector <=> {sourceVector}
                LIMIT {limit}
                """)
            .ToListAsync();

        return results;
    }

    public async Task<IReadOnlyList<SearchResult>> DiscoverAsync(
        int recentCount = 3, int limit = 5)
    {
        var recentNotes = await _db.Notes
            .Where(n => n.Embedding != null)
            .OrderByDescending(n => n.UpdatedAt)
            .Take(recentCount)
            .ToListAsync();

        if (recentNotes.Count == 0)
            return Array.Empty<SearchResult>();

        var recentIds = recentNotes.Select(n => n.Id).ToHashSet();

        // Average the embeddings of recent notes
        var firstEmbedding = recentNotes[0].Embedding!.ToArray();
        var dim = firstEmbedding.Length;
        var avgEmbedding = new float[dim];
        foreach (var note in recentNotes)
        {
            var arr = note.Embedding!.ToArray();
            for (var i = 0; i < dim; i++)
                avgEmbedding[i] += arr[i];
        }
        for (var i = 0; i < dim; i++)
            avgEmbedding[i] /= recentNotes.Count;

        var avgVector = new Vector(avgEmbedding);
        var minSimilarity = _weights.MinimumSimilarity;

        var results = await _db.Database
            .SqlQuery<SearchResult>($"""
                SELECT "Id" AS "NoteId",
                       "Title",
                       CASE WHEN LENGTH("Content") > 200
                            THEN LEFT("Content", 200) || '...'
                            ELSE "Content"
                       END AS "Snippet",
                       (1.0 - ("Embedding"::vector <=> {avgVector}))::float8 AS "Rank"
                FROM "Notes"
                WHERE "Embedding" IS NOT NULL
                  AND 1.0 - ("Embedding"::vector <=> {avgVector}) >= {minSimilarity}
                ORDER BY "Embedding"::vector <=> {avgVector}
                LIMIT {limit + recentCount}
                """)
            .ToListAsync();

        // Exclude the recent notes used as query base
        return results
            .Where(r => !recentIds.Contains(r.NoteId))
            .Take(limit)
            .ToList();
    }

    private static List<SearchResult> Normalize(IReadOnlyList<SearchResult> results)
    {
        if (results.Count == 0)
            return [];

        var maxRank = results.Max(r => r.Rank);
        var minRank = results.Min(r => r.Rank);
        var range = maxRank - minRank;

        return results.Select(r => new SearchResult
        {
            NoteId = r.NoteId,
            Title = r.Title,
            Snippet = r.Snippet,
            Rank = range == 0 ? 1.0 : (r.Rank - minRank) / range,
        }).ToList();
    }
}
