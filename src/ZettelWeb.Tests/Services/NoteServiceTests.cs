using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class NoteServiceTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    private static ChannelEmbeddingQueue CreateQueue() => new();

    [Fact]
    public async Task CreateAsync_PersistsNoteWithTitleAndContent()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("My Title", "My content");

        Assert.Equal("My Title", note.Title);
        Assert.Equal("My content", note.Content);

        var saved = await context.Notes.FindAsync(note.Id);
        Assert.NotNull(saved);
        Assert.Equal("My Title", saved.Title);
    }

    [Fact]
    public async Task CreateAsync_GeneratesZettelkastenIdWithSuffix()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Title", "Content");

        // ID format: 17-digit timestamp + 4-digit random suffix = 21 chars
        Assert.Matches(@"^\d{21}$", note.Id);
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniqueIdsForRapidCreation()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        var service = new NoteService(context, queue);

        // Create multiple notes in rapid succession (same millisecond possible)
        var ids = new HashSet<string>();
        for (var i = 0; i < 10; i++)
        {
            var note = await service.CreateAsync($"Title {i}", $"Content {i}");
            Assert.True(ids.Add(note.Id), $"Duplicate ID detected: {note.Id}");
        }

        Assert.Equal(10, ids.Count);
    }

    [Fact]
    public async Task CreateAsync_SetsEmbedStatusToPending()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Title", "Content");

        Assert.Equal(EmbedStatus.Pending, note.EmbedStatus);
    }

    [Fact]
    public async Task CreateAsync_SetsTimestamps()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var before = DateTime.UtcNow;
        var note = await service.CreateAsync("Title", "Content");
        var after = DateTime.UtcNow;

        Assert.InRange(note.CreatedAt, before, after);
        Assert.InRange(note.UpdatedAt, before, after);
    }

    [Fact]
    public async Task CreateAsync_EnqueuesNoteForEmbedding()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        var service = new NoteService(context, queue);

        var note = await service.CreateAsync("Title", "Content");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);
        Assert.Equal(note.Id, noteId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNoteWhenFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());
        var created = await service.CreateAsync("Title", "Content");

        var found = await service.GetByIdAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("Title", found.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var found = await service.GetByIdAsync("99999999999999");

        Assert.Null(found);
    }

    [Fact]
    public async Task ListAsync_ReturnsPagedResultWithAllNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Note 1", Content = "Content 1" },
            new Note { Id = "20260213120002", Title = "Note 2", Content = "Content 2" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync();

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyPagedResultWhenNoNotes()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync();

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ReturnsTotalCountGreaterThanPageSize()
    {
        await using var context = CreateDbContext();
        for (var i = 0; i < 10; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"2026021312{i:D004}000",
                Title = $"Note {i}",
                Content = "C",
            });
        }
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(skip: 0, take: 3);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(10, result.TotalCount);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTitleAndContent()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Old Title",
            Content = "Old Content",
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260213120000", "New Title", "New Content");

        Assert.NotNull(updated);
        Assert.Equal("New Title", updated.Title);
        Assert.Equal("New Content", updated.Content);
    }

    [Fact]
    public async Task UpdateAsync_SetsEmbedStatusToStaleWhenContentChanges()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Old Content",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260213120000", "Title", "New Content");

        Assert.NotNull(updated);
        Assert.Equal(EmbedStatus.Stale, updated.EmbedStatus);
    }

    [Fact]
    public async Task UpdateAsync_EnqueuesNoteWhenContentChanges()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Old Content",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, queue);

        await service.UpdateAsync("20260213120000", "Title", "New Content");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);
        Assert.Equal("20260213120000", noteId);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotEnqueueWhenOnlyTitleChanges()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Old Title",
            Content = "Same Content",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, queue);

        await service.UpdateAsync("20260213120000", "New Title", "Same Content");

        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task UpdateAsync_KeepsEmbedStatusWhenOnlyTitleChanges()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Old Title",
            Content = "Same Content",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260213120000", "New Title", "Same Content");

        Assert.NotNull(updated);
        Assert.Equal(EmbedStatus.Completed, updated.EmbedStatus);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesUpdatedAtTimestamp()
    {
        await using var context = CreateDbContext();
        var originalTime = DateTime.UtcNow.AddHours(-1);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Content",
            CreatedAt = originalTime,
            UpdatedAt = originalTime,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var before = DateTime.UtcNow;
        var updated = await service.UpdateAsync("20260213120000", "New Title", "Content");

        Assert.NotNull(updated);
        Assert.Equal(originalTime, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= before);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNullWhenNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var result = await service.UpdateAsync("99999999999999", "Title", "Content");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesNoteAndReturnsTrue()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "To Delete",
            Content = "Content",
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.DeleteAsync("20260213120000");

        Assert.True(result);
        Assert.Null(await context.Notes.FindAsync("20260213120000"));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var result = await service.DeleteAsync("99999999999999");

        Assert.False(result);
    }

    // ── Re-Embed Tests ────────────────────────────────────────

    [Fact]
    public async Task ReEmbedAllAsync_MarksCompletedNotesAsPending()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.ReEmbedAllAsync();

        var note = await context.Notes.FindAsync("20260213120001");
        Assert.Equal(EmbedStatus.Pending, note!.EmbedStatus);
    }

    [Fact]
    public async Task ReEmbedAllAsync_MarksStaleAndFailedAsPending()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Stale", Content = "C",
                EmbedStatus = EmbedStatus.Stale,
            },
            new Note
            {
                Id = "20260213120002", Title = "Failed", Content = "C",
                EmbedStatus = EmbedStatus.Failed, EmbedRetryCount = 3,
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.ReEmbedAllAsync();

        var stale = await context.Notes.FindAsync("20260213120001");
        var failed = await context.Notes.FindAsync("20260213120002");
        Assert.Equal(EmbedStatus.Pending, stale!.EmbedStatus);
        Assert.Equal(EmbedStatus.Pending, failed!.EmbedStatus);
    }

    [Fact]
    public async Task ReEmbedAllAsync_ResetsRetryCount()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C",
            EmbedStatus = EmbedStatus.Failed, EmbedRetryCount = 5,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.ReEmbedAllAsync();

        var note = await context.Notes.FindAsync("20260213120001");
        Assert.Equal(0, note!.EmbedRetryCount);
    }

    [Fact]
    public async Task ReEmbedAllAsync_SkipsAlreadyPendingAndProcessing()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Pending", Content = "C",
                EmbedStatus = EmbedStatus.Pending,
            },
            new Note
            {
                Id = "20260213120002", Title = "Processing", Content = "C",
                EmbedStatus = EmbedStatus.Processing,
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var count = await service.ReEmbedAllAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ReEmbedAllAsync_ReturnsCountOfQueuedNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "A", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "20260213120002", Title = "B", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "20260213120003", Title = "C", Content = "C", EmbedStatus = EmbedStatus.Pending });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var count = await service.ReEmbedAllAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ReEmbedAllAsync_EnqueuesNotes()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, queue);

        await service.ReEmbedAllAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);
        Assert.Equal("20260213120001", noteId);
    }

    [Fact]
    public async Task ReEmbedAllAsync_ClearsEmbedError()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C",
            EmbedStatus = EmbedStatus.Failed,
            EmbedError = "Previous error message",
            EmbedRetryCount = 3,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.ReEmbedAllAsync();

        var note = await context.Notes.FindAsync("20260213120001");
        Assert.Null(note!.EmbedError);
    }

    [Fact]
    public async Task ReEmbedAllAsync_DoesNotLoadEmbeddingData()
    {
        // Verifies that ReEmbedAllAsync works correctly even when notes
        // have large embedding arrays — the implementation should use
        // ExecuteUpdateAsync + select IDs only, not load full entities.
        await using var context = CreateDbContext();
        var largeEmbedding = new float[1536];
        Array.Fill(largeEmbedding, 0.5f);
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = largeEmbedding,
        });
        await context.SaveChangesAsync();
        var queue = CreateQueue();
        var service = new NoteService(context, queue);

        var count = await service.ReEmbedAllAsync();

        Assert.Equal(1, count);
        var note = await context.Notes.FindAsync("20260213120001");
        Assert.Equal(EmbedStatus.Pending, note!.EmbedStatus);
        Assert.Equal(0, note.EmbedRetryCount);
        // Embedding data should still be intact
        Assert.NotNull(note.Embedding);
        Assert.Equal(1536, note.Embedding.Length);
    }

    // ── Title Search Tests ──────────────────────────────────────

    [Fact]
    public async Task SearchTitlesAsync_ReturnsTitlesMatchingPrefix()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "C" },
            new Note { Id = "20260213120002", Title = "Learning Go", Content = "C" },
            new Note { Id = "20260213120003", Title = "Cooking Pasta", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("Learning");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("Learning", r.Title));
    }

    [Fact]
    public async Task SearchTitlesAsync_IsCaseInsensitive()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("learning");

        Assert.Single(results);
        Assert.Equal("Learning Rust", results[0].Title);
    }

    [Fact]
    public async Task SearchTitlesAsync_ReturnsEmptyForNoMatch()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("xyz");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchTitlesAsync_ReturnsNoteIdAndTitle()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("Learning");

        Assert.Single(results);
        Assert.Equal("20260213120001", results[0].NoteId);
        Assert.Equal("Learning Rust", results[0].Title);
    }

    [Fact]
    public async Task SearchTitlesAsync_LimitsResults()
    {
        await using var context = CreateDbContext();
        for (var i = 0; i < 15; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"2026021312{i:D004}",
                Title = $"Note {i}",
                Content = "C",
            });
        }
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("Note", limit: 5);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchTitlesAsync_ReturnsEmptyForEmptyPrefix()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var results = await service.SearchTitlesAsync("");

        Assert.Empty(results);
    }

    // ── Fleeting Notes Tests ──────────────────────────────────────

    [Fact]
    public async Task CreateFleetingAsync_SetsStatusToFleeting()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("Check this out", "web");

        Assert.Equal(NoteStatus.Fleeting, note.Status);
        Assert.Equal("web", note.Source);
    }

    [Fact]
    public async Task CreateFleetingAsync_AutoGeneratesTitleFromFirstLine()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("My quick thought\nMore details here", "web");

        Assert.Equal("My quick thought", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_TruncatesLongTitle()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var longLine = new string('a', 100);
        var note = await service.CreateFleetingAsync(longLine, "web");

        Assert.Equal(63, note.Title.Length); // 60 chars + "..."
        Assert.EndsWith("...", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_UsesDefaultTitleForEmptyContent()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("   ", "web");

        Assert.Equal("Fleeting note", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_EnqueuesForEmbedding()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        var service = new NoteService(context, queue);

        var note = await service.CreateFleetingAsync("Some thought", "telegram");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);
        Assert.Equal(note.Id, noteId);
    }

    [Fact]
    public async Task CreateFleetingAsync_PersistsTags()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("Note with tags", "web",
            new[] { "idea", "rust" });

        var saved = await context.Notes.Include(n => n.Tags).FirstAsync(n => n.Id == note.Id);
        Assert.Equal(2, saved.Tags.Count);
    }

    [Fact]
    public async Task ListAsync_WithFleetingStatus_ReturnsOnlyFleetingNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Permanent", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "Fleeting", Content = "C",
                Status = NoteStatus.Fleeting },
            new Note { Id = "20260213120003", Title = "Also Fleeting", Content = "C",
                Status = NoteStatus.Fleeting });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(status: NoteStatus.Fleeting);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, n => Assert.Equal(NoteStatus.Fleeting, n.Status));
    }

    [Fact]
    public async Task ListAsync_WithFleetingStatus_OrdersByCreatedAtDescending()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Old", Content = "C",
                Status = NoteStatus.Fleeting,
                CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new Note { Id = "20260213120002", Title = "New", Content = "C",
                Status = NoteStatus.Fleeting,
                CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(status: NoteStatus.Fleeting);

        Assert.Equal("20260213120002", result.Items[0].Id);
        Assert.Equal("20260213120001", result.Items[1].Id);
    }

    [Fact]
    public async Task PromoteAsync_SetsStatusToPermanent()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.PromoteAsync("20260213120001");

        Assert.NotNull(result);
        Assert.Equal(NoteStatus.Permanent, result.Status);

        var saved = await context.Notes.FindAsync("20260213120001");
        Assert.Equal(NoteStatus.Permanent, saved!.Status);
    }

    [Fact]
    public async Task PromoteAsync_UpdatesUpdatedAt()
    {
        await using var context = CreateDbContext();
        var oldTime = DateTime.UtcNow.AddHours(-1);
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
            UpdatedAt = oldTime,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var before = DateTime.UtcNow;
        var result = await service.PromoteAsync("20260213120001");

        Assert.NotNull(result);
        Assert.True(result.UpdatedAt >= before);
    }

    [Fact]
    public async Task PromoteAsync_ReturnsNullWhenNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var result = await service.PromoteAsync("99999999999999");

        Assert.Null(result);
    }

    [Fact]
    public async Task CountFleetingAsync_ReturnsCountOfFleetingNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "P", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "F1", Content = "C",
                Status = NoteStatus.Fleeting },
            new Note { Id = "20260213120003", Title = "F2", Content = "C",
                Status = NoteStatus.Fleeting });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var count = await service.CountFleetingAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountFleetingAsync_ReturnsZeroWhenNone()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note { Id = "20260213120001", Title = "P", Content = "C",
            Status = NoteStatus.Permanent });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var count = await service.CountFleetingAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ListAsync_FiltersbyStatus()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "P", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "F", Content = "C",
                Status = NoteStatus.Fleeting });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var permanentOnly = await service.ListAsync(status: NoteStatus.Permanent);
        var fleetingOnly = await service.ListAsync(status: NoteStatus.Fleeting);
        var all = await service.ListAsync();

        Assert.Single(permanentOnly.Items);
        Assert.Equal("P", permanentOnly.Items[0].Title);
        Assert.Single(fleetingOnly.Items);
        Assert.Equal("F", fleetingOnly.Items[0].Title);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task CreateAsync_DefaultsStatusToPermanent()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Title", "Content");

        Assert.Equal(NoteStatus.Permanent, note.Status);
    }

    // ── Note Type Tests ──────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DefaultsNoteTypeToRegular()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Title", "Content");

        Assert.Equal(NoteType.Regular, note.NoteType);
    }

    [Fact]
    public async Task CreateAsync_WithNoteType_SetsType()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Hub Note", "Links to other notes",
            noteType: NoteType.Structure);

        Assert.Equal(NoteType.Structure, note.NoteType);
        var saved = await context.Notes.FindAsync(note.Id);
        Assert.Equal(NoteType.Structure, saved!.NoteType);
    }

    [Fact]
    public async Task CreateAsync_WithSourceType_PersistsSourceMetadata()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateAsync("Clean Code", "Notes on the book",
            noteType: NoteType.Source,
            sourceAuthor: "Robert C. Martin",
            sourceTitle: "Clean Code",
            sourceUrl: "https://example.com/clean-code",
            sourceYear: 2008,
            sourceType: "book");

        Assert.Equal(NoteType.Source, note.NoteType);
        Assert.Equal("Robert C. Martin", note.SourceAuthor);
        Assert.Equal("Clean Code", note.SourceTitle);
        Assert.Equal("https://example.com/clean-code", note.SourceUrl);
        Assert.Equal(2008, note.SourceYear);
        Assert.Equal("book", note.SourceType);
    }

    [Fact]
    public async Task UpdateAsync_WithNoteType_ChangesType()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260215120000", Title = "My Note", Content = "C",
            NoteType = NoteType.Regular,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260215120000", "My Note", "C",
            noteType: NoteType.Structure);

        Assert.NotNull(updated);
        Assert.Equal(NoteType.Structure, updated.NoteType);
    }

    [Fact]
    public async Task UpdateAsync_WithSourceMetadata_PersistsFields()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260215120000", Title = "Reference", Content = "C",
            NoteType = NoteType.Regular,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260215120000", "Reference", "C",
            noteType: NoteType.Source,
            sourceAuthor: "Sönke Ahrens",
            sourceTitle: "How to Take Smart Notes",
            sourceYear: 2017,
            sourceType: "book");

        Assert.NotNull(updated);
        Assert.Equal("Sönke Ahrens", updated.SourceAuthor);
        Assert.Equal("How to Take Smart Notes", updated.SourceTitle);
        Assert.Equal(2017, updated.SourceYear);
        Assert.Equal("book", updated.SourceType);
    }

    [Fact]
    public async Task UpdateAsync_ChangingFromSource_ClearsSourceFields()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260215120000", Title = "Was Source", Content = "C",
            NoteType = NoteType.Source,
            SourceAuthor = "Author",
            SourceTitle = "Title",
            SourceUrl = "https://example.com",
            SourceYear = 2020,
            SourceType = "book",
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var updated = await service.UpdateAsync("20260215120000", "Was Source", "C",
            noteType: NoteType.Regular);

        Assert.NotNull(updated);
        Assert.Equal(NoteType.Regular, updated.NoteType);
        Assert.Null(updated.SourceAuthor);
        Assert.Null(updated.SourceTitle);
        Assert.Null(updated.SourceUrl);
        Assert.Null(updated.SourceYear);
        Assert.Null(updated.SourceType);
    }

    [Fact]
    public async Task ListAsync_FiltersByNoteType()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260215120001", Title = "Regular", Content = "C",
                NoteType = NoteType.Regular },
            new Note { Id = "20260215120002", Title = "Structure", Content = "C",
                NoteType = NoteType.Structure },
            new Note { Id = "20260215120003", Title = "Source", Content = "C",
                NoteType = NoteType.Source });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var structureOnly = await service.ListAsync(noteType: NoteType.Structure);
        var sourceOnly = await service.ListAsync(noteType: NoteType.Source);

        Assert.Single(structureOnly.Items);
        Assert.Equal("Structure", structureOnly.Items[0].Title);
        Assert.Single(sourceOnly.Items);
        Assert.Equal("Source", sourceOnly.Items[0].Title);
    }

    [Fact]
    public async Task ListAsync_WithNoNoteTypeFilter_ReturnsAll()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260215120001", Title = "Regular", Content = "C",
                NoteType = NoteType.Regular },
            new Note { Id = "20260215120002", Title = "Structure", Content = "C",
                NoteType = NoteType.Structure });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var all = await service.ListAsync();

        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task PromoteAsync_WithTargetType_SetsNoteType()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260215120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.PromoteAsync("20260215120001",
            targetType: NoteType.Source);

        Assert.NotNull(result);
        Assert.Equal(NoteStatus.Permanent, result.Status);
        Assert.Equal(NoteType.Source, result.NoteType);
    }

    [Fact]
    public async Task PromoteAsync_WithoutTargetType_DefaultsToRegular()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260215120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.PromoteAsync("20260215120001");

        Assert.NotNull(result);
        Assert.Equal(NoteType.Regular, result.NoteType);
    }
}
