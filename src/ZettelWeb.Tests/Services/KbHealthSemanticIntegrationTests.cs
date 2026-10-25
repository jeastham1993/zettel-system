using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Integration tests for KbHealthService's pgvector-backed features against a
/// real PostgreSQL + pgvector container:
/// mutual k-NN semantic edges, wiki-link-only orphan detection, and duplicate
/// detection.
/// </summary>
public class KbHealthSemanticIntegrationTests : IAsyncLifetime
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

    private static KbHealthService CreateService(ZettelDbContext db) =>
        new(db, new FakeChatClient(), new ConfigurationBuilder().Build(),
            NullLogger<KbHealthService>.Instance);

    private async Task ClearNotesAsync(ZettelDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "NoteTags" """);
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "Notes" """);
    }

    private static Note Note(
        string id, string title, float[]? embedding, int ageDays = 60) => new()
    {
        Id = id,
        Title = title,
        Content = "C",
        Status = NoteStatus.Permanent,
        EmbedStatus = embedding is null ? EmbedStatus.Pending : EmbedStatus.Completed,
        Embedding = embedding,
        CreatedAt = DateTime.UtcNow.AddDays(-ageDays),
    };

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "summary")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Overview_MutualKnn_ConnectsClosePairsIntoCluster()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            Note("a", "Note A", new float[] { 1.0f, 0.05f }, ageDays: 2),
            Note("b", "Note B", new float[] { 0.99f, 0.14f }, ageDays: 2),
            Note("c", "Unrelated", new float[] { 0.0f, 1.0f }));
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync();

        // A and B pick each other as nearest neighbours → one mutual pair.
        var cluster = Assert.Single(overview.RichestClusters);
        Assert.Equal(2, cluster.NoteCount);
        Assert.Equal(0.7, overview.Scorecard.AverageSemanticLinks); // (1+1+0)/3
    }

    [Fact]
    public async Task Overview_OneDirectionalKnnEdges_DoNotConnect()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        // B sits at the centre of a tight group of five clones (x1..x5).
        // A is a moderate neighbour of that group: A's five nearest notes
        // are B/x1..x5, but none of them have A in their own top 5 —
        // every A edge is one-directional and must be dropped.
        context.Notes.AddRange(
            Note("a", "Moderate A", new float[] { 0.6f, 0.8f }, ageDays: 5),
            Note("b", "Centre B", new float[] { 1.0f, 0.0f }),
            Note("x1", "Clone 1", new float[] { 0.999f, 0.0447f }),
            Note("x2", "Clone 2", new float[] { 0.99f, 0.141f }),
            Note("x3", "Clone 3", new float[] { 0.98f, 0.199f }),
            Note("x4", "Clone 4", new float[] { 0.97f, 0.243f }),
            Note("x5", "Clone 5", new float[] { 0.95f, 0.312f }),
            Note("c", "Far C", new float[] { -1.0f, 0.0f }));
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync();

        // Only the B + x1..x5 clique is mutually connected (6 notes, 5 edges
        // each). A's one-directional edges contribute nothing.
        var cluster = Assert.Single(overview.RichestClusters);
        Assert.Equal(6, cluster.NoteCount);
        Assert.Equal(3.8, overview.Scorecard.AverageSemanticLinks); // (5×6)/8 = 3.75 → rounds to 3.8

        // A is recent with no wiki links → still an orphan even though it has
        // one-directional semantic neighbours (orphan = wiki-link graph only).
        Assert.Equal(1, overview.Scorecard.OrphanCount);
        Assert.Equal("a", Assert.Single(overview.NewAndUnconnected).Id);
    }

    [Fact]
    public async Task Overview_Orphan_WithMutualSemanticTwin_StillUnconnected()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            Note("a", "Recent Note", new float[] { 1.0f, 0.0f }, ageDays: 3),
            Note("b", "Semantic Twin", new float[] { 0.995f, 0.0999f }, ageDays: 90));
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync();

        // A and B are mutual nearest neighbours, but neither has a wiki link:
        // A (recent) must still be reported as an orphan.
        Assert.Equal(1, overview.Scorecard.OrphanCount);
        Assert.Equal("a", Assert.Single(overview.NewAndUnconnected).Id);
        Assert.Equal(0, overview.Scorecard.AverageWikiLinks);
        Assert.Equal(1.0, overview.Scorecard.AverageSemanticLinks);
    }

    [Fact]
    public async Task Overview_Duplicates_FlagsNearIdenticalNotes()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            Note("dup1", "Efficiency is an interesting economic idea", new float[] { 0.8f, 0.6f }),
            Note("dup2", "Efficiency is an interesting economic idea (copy)", new float[] { 0.8001f, 0.6f }),
            Note("other", "Unrelated Note", new float[] { 0.0f, 1.0f }));
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync();

        var pair = Assert.Single(overview.Duplicates);
        Assert.Equal("dup1", pair.FirstNoteId);
        Assert.Equal("dup2", pair.SecondNoteId);
        Assert.True(pair.Similarity > 0.95);
        Assert.Contains("Efficiency", pair.FirstTitle);
    }

    [Fact]
    public async Task Overview_Duplicates_NoneWhenEmbeddingsDiffer()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            Note("n1", "Note One", new float[] { 1.0f, 0.0f }),
            Note("n2", "Note Two", new float[] { 0.7071f, 0.7071f }));
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync();

        Assert.Empty(overview.Duplicates);
    }
}
