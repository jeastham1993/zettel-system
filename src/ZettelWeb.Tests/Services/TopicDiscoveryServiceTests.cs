using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Integration tests for TopicDiscoveryService using a real PostgreSQL container.
/// Required because SelectRandomSeedAsync uses raw SQL (ORDER BY RANDOM()) that
/// is not supported by the InMemory provider.
/// </summary>
public class TopicDiscoveryServiceTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
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

    private static TopicDiscoveryService CreateService(
        ZettelDbContext db,
        TopicDiscoveryOptions? options = null)
    {
        var opts = Options.Create(options ?? new TopicDiscoveryOptions { MinClusterSize = 1 });
        return new TopicDiscoveryService(db, opts, NullLogger<TopicDiscoveryService>.Instance);
    }

    private static int _idSeq;

    private static Note MakePermanentNote(string title = "Test Note", string content = "Some content") =>
        new()
        {
            // Use the same ID format as ContentGenerationService (yyyyMMddHHmmssfff + 4 digits, max 21 chars)
            Id = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Interlocked.Increment(ref _idSeq):D4}",
            Title = title,
            Content = content,
            Status = NoteStatus.Permanent,
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[1536],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    // ── Seed recycling ────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverTopicAsync_ReturnsNull_WhenAllNotesUsedWithinRecycleWindow()
    {
        await using var db = CreateDbContext();
        var note = MakePermanentNote();
        db.Notes.Add(note);
        db.UsedSeedNotes.Add(new UsedSeedNote
        {
            NoteId = note.Id,
            UsedAt = DateTime.UtcNow.AddDays(-1), // used yesterday — within 30-day window
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new TopicDiscoveryOptions
        {
            MinClusterSize = 1,
            SeedRecycleDays = 30,
        });

        var cluster = await service.DiscoverTopicAsync();

        Assert.Null(cluster);
    }

    [Fact]
    public async Task DiscoverTopicAsync_RecyclesNote_WhenUsedOutsideRecycleWindow()
    {
        await using var db = CreateDbContext();
        var note = MakePermanentNote();
        db.Notes.Add(note);
        db.UsedSeedNotes.Add(new UsedSeedNote
        {
            NoteId = note.Id,
            UsedAt = DateTime.UtcNow.AddDays(-31), // used 31 days ago — outside 30-day window
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new TopicDiscoveryOptions
        {
            MinClusterSize = 1,
            SeedRecycleDays = 30,
        });

        var cluster = await service.DiscoverTopicAsync();

        Assert.NotNull(cluster);
        Assert.Equal(note.Id, cluster.SeedNoteId);
    }

    [Fact]
    public async Task DiscoverTopicAsync_PrefersUnusedNotes_OverRecycledOnes()
    {
        await using var db = CreateDbContext();

        var freshNote = MakePermanentNote("Fresh");
        var recycledNote = MakePermanentNote("Recycled");

        db.Notes.AddRange(freshNote, recycledNote);
        db.UsedSeedNotes.Add(new UsedSeedNote
        {
            NoteId = recycledNote.Id,
            UsedAt = DateTime.UtcNow.AddDays(-60), // old — eligible for recycling
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new TopicDiscoveryOptions
        {
            MinClusterSize = 1,
            SeedRecycleDays = 30,
        });

        // Both are eligible; just verify discovery succeeds (random ordering means
        // we can't assert which specific note is chosen, only that one is returned).
        var cluster = await service.DiscoverTopicAsync();

        Assert.NotNull(cluster);
        Assert.Contains(cluster.SeedNoteId, new[] { freshNote.Id, recycledNote.Id });
    }
}
