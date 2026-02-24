using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Unit tests for KbHealthService using the InMemory provider.
/// Semantic edge detection (pgvector) is skipped gracefully on InMemory —
/// the tests cover wiki-link-based connectivity, orphan detection, cluster logic,
/// embed metrics, seed tracking, and wikilink insertion.
/// </summary>
public class KbHealthServiceTests
{
    private static ZettelDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static KbHealthService CreateService(ZettelDbContext db) =>
        new(db, NullLogger<KbHealthService>.Instance);

    // ── GetOverviewAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_EmptyDb_ReturnsZeroScorecard()
    {
        var db = CreateDb();
        var svc = CreateService(db);

        var overview = await svc.GetOverviewAsync();

        Assert.Equal(0, overview.Scorecard.TotalNotes);
        Assert.Equal(0, overview.Scorecard.OrphanCount);
        Assert.Equal(0, overview.Scorecard.EmbeddedPercent);
        Assert.Equal(0, overview.Scorecard.AverageConnections);
    }

    [Fact]
    public async Task GetOverview_TotalNotes_CountsOnlyPermanentNotes()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "p1", Title = "Permanent", Content = "C", Status = NoteStatus.Permanent },
            new Note { Id = "f1", Title = "Fleeting", Content = "C", Status = NoteStatus.Fleeting });
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Equal(1, overview.Scorecard.TotalNotes);
    }

    [Fact]
    public async Task GetOverview_EmbeddedPercent_BasedOnCompletedStatus()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "A", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "n2", Title = "B", Content = "C", EmbedStatus = EmbedStatus.Pending },
            new Note { Id = "n3", Title = "C", Content = "C", EmbedStatus = EmbedStatus.Completed });
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Equal(67, overview.Scorecard.EmbeddedPercent); // 2/3 = 66.67 → 67
    }

    [Fact]
    public async Task GetOverview_OrphanCount_OnlyRecentNotesWith0Edges()
    {
        var db = CreateDb();
        var recentOrphan = new Note { Id = "o1", Title = "Orphan", Content = "C" };
        recentOrphan.CreatedAt = DateTime.UtcNow.AddDays(-5);

        var connectedNote = new Note { Id = "c1", Title = "Hub", Content = "See [[Spoke]]" };
        var spokeNote = new Note { Id = "c2", Title = "Spoke", Content = "C" };

        db.Notes.AddRange(recentOrphan, connectedNote, spokeNote);
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        // Hub and Spoke have wiki-link edges, so they're not orphans.
        // Only the recent orphan with 0 edges counts.
        Assert.Equal(1, overview.Scorecard.OrphanCount);
    }

    [Fact]
    public async Task GetOverview_OrphanCount_OldNotesExcluded()
    {
        var db = CreateDb();
        var oldNote = new Note { Id = "o1", Title = "Old", Content = "C" };
        oldNote.CreatedAt = DateTime.UtcNow.AddDays(-60);
        db.Notes.Add(oldNote);
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Equal(0, overview.Scorecard.OrphanCount);
    }

    [Fact]
    public async Task GetOverview_NewAndUnconnected_ReturnsRecentOrphans()
    {
        var db = CreateDb();
        var recent = new Note { Id = "r1", Title = "Recent Orphan", Content = "C" };
        recent.CreatedAt = DateTime.UtcNow.AddDays(-3);
        db.Notes.Add(recent);
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Single(overview.NewAndUnconnected);
        Assert.Equal("r1", overview.NewAndUnconnected[0].Id);
    }

    [Fact]
    public async Task GetOverview_NewAndUnconnected_SortedNewestFirst()
    {
        var db = CreateDb();
        var older = new Note { Id = "o1", Title = "Older", Content = "C" };
        older.CreatedAt = DateTime.UtcNow.AddDays(-10);
        var newer = new Note { Id = "n1", Title = "Newer", Content = "C" };
        newer.CreatedAt = DateTime.UtcNow.AddDays(-2);
        db.Notes.AddRange(older, newer);
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Equal("n1", overview.NewAndUnconnected[0].Id);
        Assert.Equal("o1", overview.NewAndUnconnected[1].Id);
    }

    [Fact]
    public async Task GetOverview_RichestClusters_ReturnsTopComponentsBySize()
    {
        var db = CreateDb();
        // Cluster A: hub → spoke1, hub → spoke2
        db.Notes.AddRange(
            new Note { Id = "a1", Title = "Hub", Content = "See [[Spoke1]] and [[Spoke2]]" },
            new Note { Id = "a2", Title = "Spoke1", Content = "C" },
            new Note { Id = "a3", Title = "Spoke2", Content = "C" });
        // Cluster B: isolated
        db.Notes.Add(new Note { Id = "b1", Title = "Lone", Content = "C" });
        // Set CreatedAt old enough so orphans don't appear in NewAndUnconnected
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        var topCluster = overview.RichestClusters.First();
        Assert.Equal(3, topCluster.NoteCount);
        Assert.Equal("a1", topCluster.HubNoteId); // a1 has 2 edges, a2/a3 have 1 each
    }

    [Fact]
    public async Task GetOverview_NeverUsedAsSeeds_ExcludesUsedSeeds()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "Used", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "n2", Title = "Unused", Content = "C", EmbedStatus = EmbedStatus.Completed });
        db.UsedSeedNotes.Add(new UsedSeedNote { NoteId = "n1" });
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        Assert.Single(overview.NeverUsedAsSeeds);
        Assert.Equal("n2", overview.NeverUsedAsSeeds[0].Id);
    }

    [Fact]
    public async Task GetOverview_NeverUsedAsSeeds_ExcludesNonEmbeddedNotes()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "n1", Title = "Embedded", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "n2", Title = "Pending", Content = "C", EmbedStatus = EmbedStatus.Pending });
        await db.SaveChangesAsync();

        var overview = await CreateService(db).GetOverviewAsync();

        // Only the embedded note should appear as a potential unused seed
        Assert.Single(overview.NeverUsedAsSeeds);
        Assert.Equal("n1", overview.NeverUsedAsSeeds[0].Id);
    }

    // ── InsertWikilinkAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task InsertWikilink_AppendsLinkToOrphanContent()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "orphan", Title = "Orphan Note", Content = "<p>Some content.</p>" },
            new Note { Id = "target", Title = "Target Note", Content = "T" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var updated = await svc.InsertWikilinkAsync("orphan", "target");

        Assert.NotNull(updated);
        Assert.Contains("[[Target Note]]", updated!.Content);
    }

    [Fact]
    public async Task InsertWikilink_MarksOrphanEmbedStatusAsStale()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "orphan", Title = "O", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "target", Title = "T", Content = "C" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.InsertWikilinkAsync("orphan", "target");

        var note = await db.Notes.FindAsync("orphan");
        Assert.Equal(EmbedStatus.Stale, note!.EmbedStatus);
    }

    [Fact]
    public async Task InsertWikilink_ReturnsNullWhenOrphanNotFound()
    {
        var db = CreateDb();
        db.Notes.Add(new Note { Id = "target", Title = "T", Content = "C" });
        await db.SaveChangesAsync();

        var result = await CreateService(db).InsertWikilinkAsync("missing", "target");

        Assert.Null(result);
    }

    [Fact]
    public async Task InsertWikilink_ReturnsNullWhenTargetNotFound()
    {
        var db = CreateDb();
        db.Notes.Add(new Note { Id = "orphan", Title = "O", Content = "C" });
        await db.SaveChangesAsync();

        var result = await CreateService(db).InsertWikilinkAsync("orphan", "missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task InsertWikilink_UpdatesUpdatedAt()
    {
        var db = CreateDb();
        var before = DateTime.UtcNow.AddSeconds(-1);
        db.Notes.AddRange(
            new Note { Id = "orphan", Title = "O", Content = "C" },
            new Note { Id = "target", Title = "T", Content = "C" });
        await db.SaveChangesAsync();

        var updated = await CreateService(db).InsertWikilinkAsync("orphan", "target");

        Assert.True(updated!.UpdatedAt >= before);
    }

    // ── GetNotesWithoutEmbeddingsAsync ──────────────────────────────────────

    [Fact]
    public async Task GetNotesWithoutEmbeddings_ReturnsOnlyPermanentNonCompletedNotes()
    {
        var db = CreateDb();
        db.Notes.AddRange(
            new Note { Id = "c1", Title = "Completed", Content = "C", Status = NoteStatus.Permanent, EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "p1", Title = "Pending", Content = "C", Status = NoteStatus.Permanent, EmbedStatus = EmbedStatus.Pending },
            new Note { Id = "f1", Title = "Failed", Content = "C", Status = NoteStatus.Permanent, EmbedStatus = EmbedStatus.Failed, EmbedError = "timeout" },
            new Note { Id = "fl1", Title = "Fleeting", Content = "C", Status = NoteStatus.Fleeting, EmbedStatus = EmbedStatus.Pending });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetNotesWithoutEmbeddingsAsync();

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, n => n.Id == "c1");
        Assert.DoesNotContain(result, n => n.Id == "fl1");
    }

    [Fact]
    public async Task GetNotesWithoutEmbeddings_IncludesEmbedErrorInResult()
    {
        var db = CreateDb();
        db.Notes.Add(new Note
        {
            Id = "f1", Title = "Failed", Content = "C",
            Status = NoteStatus.Permanent,
            EmbedStatus = EmbedStatus.Failed,
            EmbedError = "connection refused"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetNotesWithoutEmbeddingsAsync();

        Assert.Single(result);
        Assert.Equal("connection refused", result[0].EmbedError);
        Assert.Equal(EmbedStatus.Failed, result[0].EmbedStatus);
    }

    [Fact]
    public async Task GetNotesWithoutEmbeddings_EmptyWhenAllCompleted()
    {
        var db = CreateDb();
        db.Notes.Add(new Note { Id = "n1", Title = "Done", Content = "C", Status = NoteStatus.Permanent, EmbedStatus = EmbedStatus.Completed });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetNotesWithoutEmbeddingsAsync();

        Assert.Empty(result);
    }

    // ── RequeueEmbeddingAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RequeueEmbedding_SetsPendingStatusAndClearsError()
    {
        var db = CreateDb();
        db.Notes.Add(new Note
        {
            Id = "f1", Title = "Failed", Content = "C",
            EmbedStatus = EmbedStatus.Failed,
            EmbedError = "timeout",
            EmbedRetryCount = 3
        });
        await db.SaveChangesAsync();

        var count = await CreateService(db).RequeueEmbeddingAsync("f1");

        Assert.Equal(1, count);
        var note = await db.Notes.FindAsync("f1");
        Assert.Equal(EmbedStatus.Pending, note!.EmbedStatus);
        Assert.Null(note.EmbedError);
        Assert.Equal(0, note.EmbedRetryCount);
    }

    [Fact]
    public async Task RequeueEmbedding_ReturnsZeroWhenNoteNotFound()
    {
        var db = CreateDb();
        var count = await CreateService(db).RequeueEmbeddingAsync("missing");
        Assert.Equal(0, count);
    }

    // ── GetConnectionSuggestionsAsync ───────────────────────────────────────

    [Fact]
    public async Task GetConnectionSuggestions_ReturnsEmptyWhenNoteNotFound()
    {
        var db = CreateDb();
        var svc = CreateService(db);

        var suggestions = await svc.GetConnectionSuggestionsAsync("nonexistent");

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetConnectionSuggestions_ReturnsEmptyWhenNoteHasNoEmbedding()
    {
        var db = CreateDb();
        db.Notes.Add(new Note { Id = "n1", Title = "No Embed", Content = "C", Embedding = null });
        await db.SaveChangesAsync();

        var suggestions = await CreateService(db).GetConnectionSuggestionsAsync("n1");

        Assert.Empty(suggestions);
    }
}
