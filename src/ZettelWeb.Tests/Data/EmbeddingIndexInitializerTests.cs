using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Tests.Data;

public class EmbeddingIndexInitializerTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();

        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private ZettelDbContext CreateDbContext()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), o => o.UseVector())
            .Options;
        return new ZettelDbContext(options);
    }

    private static async Task<Note> SeedNoteAsync(ZettelDbContext db, string id, float[] embedding, string status)
    {
        var note = new Note
        {
            Id = id,
            Title = "Test note",
            Content = "Some content that is long enough",
            Embedding = embedding,
            EmbedStatus = Enum.Parse<EmbedStatus>(status),
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    [Fact]
    public async Task EnsureIndex_CreatesIndexWhenDimensionsMatch()
    {
        await using var db = CreateDbContext();
        var note = await SeedNoteAsync(db, "n1", [0.1f, 0.2f, 0.3f], "Completed");

        db.EnsureIndex(3, NullLogger.Instance);

        var indexDef = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = {EmbeddingIndexInitializer.IndexName}")
            .FirstAsync();
        Assert.Contains("vector(3)", indexDef);

        // Matching vectors are left alone
        db.Entry(note).Reload();
        Assert.Equal(EmbedStatus.Completed, note.EmbedStatus);
        Assert.NotNull(note.Embedding);
    }

    [Fact]
    public async Task EnsureIndex_DimensionChangeDropsIndexAndRequeuesStaleVectors()
    {
        // Production shape: all embeddings share the old dimension and a
        // dimension-bound index exists over them
        await using var db = CreateDbContext();
        var first = await SeedNoteAsync(db, "first", [0.1f, 0.2f], "Completed");
        var second = await SeedNoteAsync(db, "second", [0.5f, 0.6f], "Completed");
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE INDEX idx_notes_embedding_hnsw ON \"Notes\" USING hnsw ((\"Embedding\"::vector(2)) vector_cosine_ops) WHERE \"Embedding\" IS NOT NULL;");

        db.EnsureIndex(3, NullLogger.Instance);

        var indexDef = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = {EmbeddingIndexInitializer.IndexName}")
            .FirstAsync();
        Assert.Contains("vector(3)", indexDef);

        // Both 2-dim vectors were requeued
        db.Entry(db.Notes.Find("first")!).Reload();
        db.Entry(db.Notes.Find("second")!).Reload();
        Assert.Null(first.Embedding);
        Assert.Equal(EmbedStatus.Pending, first.EmbedStatus);
        Assert.Null(second.Embedding);
        Assert.Equal(EmbedStatus.Pending, second.EmbedStatus);
    }

    [Fact]
    public async Task EnsureIndex_RequeuesOnlyMismatchedVectorsWhenNoIndexExists()
    {
        // Mixed dimensions can only coexist without a dimension-bound index
        // (e.g. a previous index build was skipped); only the stale ones churn
        await using var db = CreateDbContext();
        var stale = await SeedNoteAsync(db, "stale", [0.1f, 0.2f], "Completed");
        var fresh = await SeedNoteAsync(db, "fresh", [0.4f, 0.5f, 0.6f], "Completed");

        db.EnsureIndex(3, NullLogger.Instance);

        var indexDef = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = {EmbeddingIndexInitializer.IndexName}")
            .FirstAsync();
        Assert.Contains("vector(3)", indexDef);

        db.Entry(db.Notes.Find("stale")!).Reload();
        db.Entry(db.Notes.Find("fresh")!).Reload();
        Assert.Null(stale.Embedding);
        Assert.Equal(EmbedStatus.Pending, stale.EmbedStatus);
        Assert.NotNull(fresh.Embedding);
        Assert.Equal(EmbedStatus.Completed, fresh.EmbedStatus);
    }

    [Fact]
    public async Task EnsureIndex_EmptyTableCreatesIndexWithoutChurn()
    {
        await using var db = CreateDbContext();

        db.EnsureIndex(1536, NullLogger.Instance);

        var indexDef = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = {EmbeddingIndexInitializer.IndexName}")
            .FirstAsync();
        Assert.Contains("vector(1536)", indexDef);
        Assert.Equal(0, await db.Notes.CountAsync());
    }
}
