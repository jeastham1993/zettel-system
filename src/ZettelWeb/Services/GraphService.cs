using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public record SemanticEdge(string SourceId, string TargetId, double Similarity);

public class GraphService : IGraphService
{
    private readonly ZettelDbContext _db;
    private readonly ILogger<GraphService> _logger;

    public GraphService(ZettelDbContext db, ILogger<GraphService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<GraphData> BuildGraphAsync(double semanticThreshold = 0.8)
    {
        // Load only Id, Title, Content — do NOT load Embedding into memory.
        var notes = await _db.Notes
            .AsNoTracking()
            .Select(n => new { n.Id, n.Title, n.Content })
            .ToListAsync();
        if (notes.Count == 0)
            return new GraphData();

        var titleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in notes)
            titleToId.TryAdd(n.Title, n.Id);
        var edges = new List<GraphEdge>();

        // Detect wiki-link edges
        foreach (var note in notes)
        {
            foreach (var linkedTitle in WikiLinkParser.ExtractLinkedTitles(note.Content))
            {
                if (titleToId.TryGetValue(linkedTitle, out var targetId) && targetId != note.Id)
                {
                    edges.Add(new GraphEdge
                    {
                        Source = note.Id,
                        Target = targetId,
                        Type = "wikilink",
                        Weight = 1.0,
                    });
                }
            }
        }

        // Detect semantic edges via pgvector nearest-neighbor SQL query.
        // Uses CROSS JOIN LATERAL with top-5 per note to avoid O(n^2) in-memory comparison.
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
                        ORDER BY n1."Embedding"::vector <=> n2."Embedding"::vector
                        LIMIT 5
                    ) n2
                    WHERE n1."Embedding" IS NOT NULL
                      AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > {semanticThreshold}
                    """)
                .ToListAsync();

            foreach (var se in semanticEdges)
            {
                edges.Add(new GraphEdge
                {
                    Source = se.SourceId,
                    Target = se.TargetId,
                    Type = "semantic",
                    Weight = se.Similarity,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic edge query failed (likely InMemory provider), skipping semantic edges");
        }

        // Build edge count per node
        var edgeCounts = new Dictionary<string, int>();
        foreach (var edge in edges)
        {
            edgeCounts[edge.Source] = edgeCounts.GetValueOrDefault(edge.Source) + 1;
            edgeCounts[edge.Target] = edgeCounts.GetValueOrDefault(edge.Target) + 1;
        }

        var graphNodes = notes.Select(n => new GraphNode
        {
            Id = n.Id,
            Title = n.Title,
            EdgeCount = edgeCounts.GetValueOrDefault(n.Id),
        }).ToList();

        return new GraphData { Nodes = graphNodes, Edges = edges };
    }

}
