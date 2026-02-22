using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class TopicDiscoveryOptions
{
    public int MaxClusterSize { get; set; } = 10;
    public int MinClusterSize { get; set; } = 3;
    public int MaxSeedRetries { get; set; } = 3;
    public double SemanticSimilarityThreshold { get; set; } = 0.75;
}

public partial class TopicDiscoveryService : ITopicDiscoveryService
{
    private readonly ZettelDbContext _db;
    private readonly ILogger<TopicDiscoveryService> _logger;
    private readonly TopicDiscoveryOptions _options;

    public TopicDiscoveryService(
        ZettelDbContext db,
        IOptions<TopicDiscoveryOptions> options,
        ILogger<TopicDiscoveryService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TopicCluster?> DiscoverTopicAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("topic.discover");

        for (var attempt = 0; attempt < _options.MaxSeedRetries; attempt++)
        {
            activity?.SetTag("topic.attempt", attempt + 1);

            var seed = await SelectRandomSeedAsync(cancellationToken);
            if (seed is null)
            {
                _logger.LogInformation("No eligible seed notes remaining");
                return null;
            }

            activity?.SetTag("topic.seed_id", seed.Id);
            _logger.LogInformation("Attempting topic discovery from seed {SeedId} (attempt {Attempt})",
                seed.Id, attempt + 1);

            var cluster = await BuildClusterAsync(seed, cancellationToken);
            if (cluster.Count >= _options.MinClusterSize)
            {
                var summary = BuildTopicSummary(cluster);
                activity?.SetTag("topic.cluster_size", cluster.Count);
                activity?.SetTag("topic.summary", summary);

                return new TopicCluster(seed.Id, cluster, summary);
            }

            _logger.LogInformation(
                "Cluster from seed {SeedId} too small ({Count} < {Min}), retrying",
                seed.Id, cluster.Count, _options.MinClusterSize);
        }

        _logger.LogWarning("Failed to discover topic after {MaxRetries} attempts",
            _options.MaxSeedRetries);
        return null;
    }

    private async Task<Note?> SelectRandomSeedAsync(CancellationToken cancellationToken)
    {
        var usedSeedIds = _db.UsedSeedNotes.Select(u => u.NoteId);

        // Use raw SQL for ORDER BY RANDOM() which isn't directly supported by EF LINQ
        var eligibleIds = await _db.Database
            .SqlQuery<string>($"""
                SELECT "Id" AS "Value"
                FROM "Notes"
                WHERE "Status" = 'Permanent'
                  AND "EmbedStatus" = 'Completed'
                  AND "Embedding" IS NOT NULL
                  AND "Id" NOT IN (SELECT "NoteId" FROM "UsedSeedNotes")
                ORDER BY RANDOM()
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);

        if (eligibleIds.Count == 0)
            return null;

        return await _db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == eligibleIds[0], cancellationToken);
    }

    private async Task<List<Note>> BuildClusterAsync(
        Note seed, CancellationToken cancellationToken)
    {
        var clusterIds = new HashSet<string> { seed.Id };
        var cluster = new List<Note> { seed };

        // 1. Follow wikilinks from the seed note
        var wikilinkNotes = await FindWikilinkTargetsAsync(
            seed.Content, clusterIds, cancellationToken);
        foreach (var note in wikilinkNotes)
        {
            if (cluster.Count >= _options.MaxClusterSize) break;
            clusterIds.Add(note.Id);
            cluster.Add(note);
        }

        // 2. Find semantically similar notes to the seed
        if (cluster.Count < _options.MaxClusterSize && seed.Embedding is not null)
        {
            var semanticNotes = await FindSemanticNeighboursAsync(
                seed, clusterIds, cancellationToken);
            foreach (var note in semanticNotes)
            {
                if (cluster.Count >= _options.MaxClusterSize) break;
                clusterIds.Add(note.Id);
                cluster.Add(note);
            }
        }

        // 3. One more hop: follow wikilinks from first-level related notes
        if (cluster.Count < _options.MaxClusterSize)
        {
            var firstLevelNotes = cluster.Skip(1).ToList();
            foreach (var related in firstLevelNotes)
            {
                if (cluster.Count >= _options.MaxClusterSize) break;
                var secondHop = await FindWikilinkTargetsAsync(
                    related.Content, clusterIds, cancellationToken);
                foreach (var note in secondHop)
                {
                    if (cluster.Count >= _options.MaxClusterSize) break;
                    clusterIds.Add(note.Id);
                    cluster.Add(note);
                }
            }
        }

        return cluster;
    }

    private async Task<List<Note>> FindWikilinkTargetsAsync(
        string content, IReadOnlySet<string> excludeIds,
        CancellationToken cancellationToken)
    {
        var linkedTitles = new List<string>();
        foreach (Match match in WikiLinkRegex().Matches(content))
        {
            linkedTitles.Add(match.Groups[1].Value);
        }

        if (linkedTitles.Count == 0)
            return [];

        return await _db.Notes
            .AsNoTracking()
            .Where(n => linkedTitles.Contains(n.Title)
                        && !excludeIds.Contains(n.Id)
                        && n.Status == NoteStatus.Permanent)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Note>> FindSemanticNeighboursAsync(
        Note source, IReadOnlySet<string> excludeIds,
        CancellationToken cancellationToken)
    {
        if (source.Embedding is null)
            return [];

        var sourceVector = new Vector(source.Embedding);
        var threshold = _options.SemanticSimilarityThreshold;
        var limit = _options.MaxClusterSize;

        try
        {
            // Query for semantically similar note IDs
            var similarIds = await _db.Database
                .SqlQuery<string>($"""
                    SELECT "Id" AS "Value"
                    FROM "Notes"
                    WHERE "Embedding" IS NOT NULL
                      AND "Id" != {source.Id}
                      AND "Status" = 'Permanent'
                      AND (1.0 - ("Embedding"::vector <=> {sourceVector})) >= {threshold}
                    ORDER BY "Embedding"::vector <=> {sourceVector}
                    LIMIT {limit}
                    """)
                .ToListAsync(cancellationToken);

            var filteredIds = similarIds.Where(id => !excludeIds.Contains(id)).ToList();
            if (filteredIds.Count == 0)
                return [];

            return await _db.Notes
                .AsNoTracking()
                .Where(n => filteredIds.Contains(n.Id))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Semantic neighbour query failed, skipping semantic edges");
            return [];
        }
    }

    private static string BuildTopicSummary(List<Note> cluster)
    {
        var titles = cluster
            .Select(n => n.Title)
            .Take(5)
            .ToList();

        var summary = "Notes on " + string.Join(", ", titles.Take(titles.Count - 1));
        if (titles.Count > 1)
            summary += ", and " + titles[^1];

        if (cluster.Count > 5)
            summary += $" (+{cluster.Count - 5} more)";

        return summary;
    }

    [GeneratedRegex(@"\[\[([^\]]+)\]\]")]
    private static partial Regex WikiLinkRegex();
}
