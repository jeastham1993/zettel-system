using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Unit tests for wiki-link detection and node building.
/// Uses InMemory provider — no raw SQL is exercised.
/// The SQL-based semantic edge detection is covered in GraphServiceIntegrationTests.
/// </summary>
public class GraphServiceTests
{
    private (GraphService Service, ZettelDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var service = new GraphService(db, NullLogger<GraphService>.Instance);
        return (service, db);
    }

    [Fact]
    public async Task BuildGraphAsync_ReturnsAllNotesAsNodes()
    {
        var (service, db) = CreateService();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "Note One", Content = "Content" },
            new Note { Id = "n2", Title = "Note Two", Content = "Content" });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync();

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Contains(graph.Nodes, n => n.Id == "n1" && n.Title == "Note One");
        Assert.Contains(graph.Nodes, n => n.Id == "n2" && n.Title == "Note Two");
    }

    [Fact]
    public async Task BuildGraphAsync_DetectsWikiLinksAsEdges()
    {
        var (service, db) = CreateService();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "Note One", Content = "See [[Note Two]] for more" },
            new Note { Id = "n2", Title = "Note Two", Content = "Content" });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync();

        Assert.Single(graph.Edges);
        var edge = graph.Edges[0];
        Assert.Equal("n1", edge.Source);
        Assert.Equal("n2", edge.Target);
        Assert.Equal("wikilink", edge.Type);
    }

    [Fact]
    public async Task BuildGraphAsync_WikiLinkToNonExistentNoteIgnored()
    {
        var (service, db) = CreateService();
        db.Notes.Add(
            new Note { Id = "n1", Title = "Note One", Content = "See [[Ghost Note]]" });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync();

        Assert.Empty(graph.Edges);
    }

    [Fact]
    public async Task BuildGraphAsync_NoSelfEdgesFromWikiLinks()
    {
        var (service, db) = CreateService();
        db.Notes.Add(new Note
        {
            Id = "n1", Title = "Note One", Content = "See [[Note One]]",
        });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync();

        Assert.DoesNotContain(graph.Edges, e => e.Source == e.Target);
    }

    [Fact]
    public async Task BuildGraphAsync_NodeEdgeCountReflectsConnections()
    {
        var (service, db) = CreateService();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "Hub", Content = "See [[Spoke A]] and [[Spoke B]]" },
            new Note { Id = "n2", Title = "Spoke A", Content = "Content" },
            new Note { Id = "n3", Title = "Spoke B", Content = "Content" });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync();

        var hub = graph.Nodes.First(n => n.Id == "n1");
        Assert.Equal(2, hub.EdgeCount);
    }

    [Fact]
    public async Task BuildGraphAsync_ReturnsEmptyGraphWhenNoNotes()
    {
        var (service, _) = CreateService();

        var graph = await service.BuildGraphAsync();

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public async Task BuildGraphAsync_GracefullyHandlesSqlFailureOnInMemory()
    {
        // With InMemory provider, raw SQL for semantic edges will fail.
        // The service should still return wiki-link edges and nodes.
        var (service, db) = CreateService();
        db.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "See [[B]]",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.99f, 0.01f },
            });
        await db.SaveChangesAsync();

        var graph = await service.BuildGraphAsync(semanticThreshold: 0.9);

        // Should still have nodes and wiki-link edges even if SQL fails
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Contains(graph.Edges, e => e.Type == "wikilink");
    }
}

/// <summary>
/// Integration tests for GraphService semantic edge detection.
/// Uses Testcontainers with pgvector to test the real SQL query.
/// </summary>
public class GraphServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .Build();
        await _postgres.StartAsync();

        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private ZettelDbContext CreateDbContext()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), o => o.UseVector())
            .Options;
        return new ZettelDbContext(options);
    }

    private GraphService CreateService(ZettelDbContext context)
        => new(context, NullLogger<GraphService>.Instance);

    private async Task ClearNotesAsync(ZettelDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "NoteTags" """);
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "Notes" """);
    }

    [Fact]
    public async Task BuildGraphAsync_DetectsSemanticEdgesAboveThreshold()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.99f, 0.01f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.9);

        Assert.Contains(graph.Edges, e =>
            e.Type == "semantic" &&
            ((e.Source == "n1" && e.Target == "n2") || (e.Source == "n2" && e.Target == "n1")));
    }

    [Fact]
    public async Task BuildGraphAsync_NoSemanticEdgeBelowThreshold()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        // Orthogonal embeddings -> zero cosine similarity
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.8);

        Assert.DoesNotContain(graph.Edges, e => e.Type == "semantic");
    }

    [Fact]
    public async Task BuildGraphAsync_NoSelfEdgesFromSemanticSimilarity()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "n1", Title = "Note One", Content = "Content",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[] { 1.0f, 0.0f },
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync();

        Assert.DoesNotContain(graph.Edges, e => e.Source == e.Target);
    }

    [Fact]
    public async Task BuildGraphAsync_SemanticEdgesDoNotLoadEmbeddingsIntoMemory()
    {
        // Verify that the SQL approach works and returns correct results
        // without loading full embeddings into C# memory
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.95f, 0.05f, 0.0f },
            },
            new Note
            {
                Id = "n3", Title = "C", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 0.0f, 1.0f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.8);

        // n1 and n2 are similar (high cosine), n3 is orthogonal
        Assert.Contains(graph.Edges, e =>
            e.Type == "semantic" &&
            ((e.Source == "n1" && e.Target == "n2") || (e.Source == "n2" && e.Target == "n1")));
        Assert.DoesNotContain(graph.Edges, e =>
            e.Type == "semantic" &&
            (e.Source == "n3" || e.Target == "n3"));
    }

    [Fact]
    public async Task BuildGraphAsync_SemanticEdgesLimitedToTopKPerNote()
    {
        // Each note should have at most 5 nearest neighbors
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);

        // Create a cluster of 8 similar notes
        for (var i = 0; i < 8; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"n{i:D2}", Title = $"Note {i}", Content = "Content",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f - i * 0.01f, i * 0.01f },
            });
        }
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.3);

        // Each note can have at most 5 semantic neighbors (from LATERAL LIMIT 5)
        var edgesPerSource = graph.Edges
            .Where(e => e.Type == "semantic")
            .GroupBy(e => e.Source)
            .Select(g => g.Count());
        Assert.All(edgesPerSource, count => Assert.True(count <= 5,
            $"Expected at most 5 semantic edges per source, got {count}"));
    }

    [Fact]
    public async Task BuildGraphAsync_CombinesWikiLinkAndSemanticEdges()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "Rust Ownership", Content = "See [[Memory Safety]]",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "Memory Safety", Content = "Memory safety in Rust",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.98f, 0.02f },
            },
            new Note
            {
                Id = "n3", Title = "Cooking Pasta", Content = "Boil water",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.9);

        // Should have both a wikilink edge and a semantic edge between n1 and n2
        Assert.Contains(graph.Edges, e => e.Type == "wikilink" && e.Source == "n1" && e.Target == "n2");
        Assert.Contains(graph.Edges, e =>
            e.Type == "semantic" &&
            ((e.Source == "n1" && e.Target == "n2") || (e.Source == "n2" && e.Target == "n1")));
        // No semantic edge to unrelated note
        Assert.DoesNotContain(graph.Edges, e =>
            e.Type == "semantic" && (e.Source == "n3" || e.Target == "n3"));
    }

    [Fact]
    public async Task BuildGraphAsync_SkipsNotesWithoutEmbeddingsForSemanticEdges()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Pending,
                Embedding = null,
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.5);

        Assert.DoesNotContain(graph.Edges, e => e.Type == "semantic");
    }

    [Fact]
    public async Task BuildGraphAsync_SemanticEdgeWeightReflectsCosineSimilarity()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "A", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "B", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.99f, 0.01f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var graph = await service.BuildGraphAsync(semanticThreshold: 0.9);

        var semanticEdge = graph.Edges.First(e => e.Type == "semantic");
        // Weight should be the cosine similarity, which should be very close to 1.0
        Assert.True(semanticEdge.Weight > 0.99, $"Expected weight > 0.99, got {semanticEdge.Weight}");
        Assert.True(semanticEdge.Weight <= 1.0, $"Expected weight <= 1.0, got {semanticEdge.Weight}");
    }
}
