using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class NoteServiceNewFeaturesTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    private static ChannelEmbeddingQueue CreateQueue() => new();

    // ── Feature 1: Tag Filtering on Note List ──────────────────

    [Fact]
    public async Task ListAsync_WithTagFilter_ReturnsOnlyNotesWithTag()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Rust Basics", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120001", Tag = "programming" },
                    new() { NoteId = "20260213120001", Tag = "rust" }
                }
            },
            new Note
            {
                Id = "20260213120002", Title = "Cooking Pasta", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120002", Tag = "cooking" }
                }
            },
            new Note
            {
                Id = "20260213120003", Title = "Go Patterns", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120003", Tag = "programming" },
                    new() { NoteId = "20260213120003", Tag = "go" }
                }
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(tag: "programming");

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, n =>
            Assert.Contains(n.Tags, t => t.Tag == "programming"));
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ReturnsEmptyWhenNoMatch()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Note", Content = "C",
            Tags = new List<NoteTag>
            {
                new() { NoteId = "20260213120001", Tag = "rust" }
            }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(tag: "python");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_WithTagAndStatusFilter_CombinesFilters()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "P-prog", Content = "C",
                Status = NoteStatus.Permanent,
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120001", Tag = "programming" }
                }
            },
            new Note
            {
                Id = "20260213120002", Title = "F-prog", Content = "C",
                Status = NoteStatus.Fleeting,
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120002", Tag = "programming" }
                }
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(
            status: NoteStatus.Permanent, tag: "programming");

        Assert.Single(result.Items);
        Assert.Equal("P-prog", result.Items[0].Title);
    }

    [Fact]
    public async Task ListAsync_WithNullTag_ReturnsAllNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "A", Content = "C" },
            new Note { Id = "20260213120002", Title = "B", Content = "C" });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.ListAsync(tag: null);

        Assert.Equal(2, result.Items.Count);
    }

    // ── Feature 2: Backlinks Endpoint ──────────────────────────

    [Fact]
    public async Task GetBacklinksAsync_FindsNotesLinkingToTarget()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Zettelkasten", Content = "A method"
            },
            new Note
            {
                Id = "20260213120002", Title = "Note Taking",
                Content = "I use [[Zettelkasten]] for notes"
            },
            new Note
            {
                Id = "20260213120003", Title = "Productivity",
                Content = "See [[Zettelkasten]] method"
            },
            new Note
            {
                Id = "20260213120004", Title = "Cooking",
                Content = "No links here"
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var backlinks = await service.GetBacklinksAsync("20260213120001");

        Assert.Equal(2, backlinks.Count);
        Assert.Contains(backlinks, b => b.Id == "20260213120002");
        Assert.Contains(backlinks, b => b.Id == "20260213120003");
    }

    [Fact]
    public async Task GetBacklinksAsync_ReturnsEmptyWhenNoBacklinks()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Lonely Note", Content = "No one links to me"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var backlinks = await service.GetBacklinksAsync("20260213120001");

        Assert.Empty(backlinks);
    }

    [Fact]
    public async Task GetBacklinksAsync_ReturnsEmptyWhenNoteNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var backlinks = await service.GetBacklinksAsync("nonexistent");

        Assert.Empty(backlinks);
    }

    [Fact]
    public async Task GetBacklinksAsync_DoesNotIncludeSelfReference()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Self Ref",
            Content = "I link to [[Self Ref]] which is me"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var backlinks = await service.GetBacklinksAsync("20260213120001");

        Assert.Empty(backlinks);
    }

    // ── Feature 3: Auto-Title for Fleeting Notes (HTML stripping) ──

    [Fact]
    public async Task CreateFleetingAsync_StripsHtmlForTitle()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync(
            "<p>Check this out</p><br><b>Important</b>", "web");

        Assert.Equal("Check this out Important", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_TruncatesTo60CharsAfterHtmlStrip()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var longText = "<p>" + new string('x', 100) + "</p>";
        var note = await service.CreateFleetingAsync(longText, "web");

        Assert.Equal(63, note.Title.Length); // 60 chars + "..."
        Assert.EndsWith("...", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_UsesFirstLineForTitle()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync(
            "<p>First line here</p>\n<p>Second line</p>", "web");

        Assert.Equal("First line here", note.Title);
        Assert.DoesNotContain("<p>", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_FallsBackToDefaultForHtmlOnlyContent()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("<br><hr>", "web");

        Assert.Equal("Fleeting note", note.Title);
    }

    [Fact]
    public async Task CreateFleetingAsync_HandlesAutoTitleFromPlainText()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var note = await service.CreateFleetingAsync("Plain text thought", "web");

        Assert.Equal("Plain text thought", note.Title);
    }

    // ── Feature 4: Inbox Merge Endpoint ────────────────────────

    [Fact]
    public async Task MergeNoteAsync_AppendsContentAndDeletesFleeting()
    {
        await using var context = CreateDbContext();
        var queue = CreateQueue();
        context.Notes.AddRange(
            new Note
            {
                Id = "fleeting01", Title = "Fleeting", Content = "Quick thought",
                Status = NoteStatus.Fleeting
            },
            new Note
            {
                Id = "target01", Title = "Target", Content = "Original content",
                Status = NoteStatus.Permanent,
                EmbedStatus = EmbedStatus.Completed
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, queue);

        var result = await service.MergeNoteAsync("fleeting01", "target01");

        Assert.NotNull(result);
        Assert.Contains("Original content", result.Content);
        Assert.Contains("Quick thought", result.Content);
        Assert.Contains("---", result.Content);
        Assert.Equal(EmbedStatus.Stale, result.EmbedStatus);

        // Fleeting note should be deleted
        Assert.Null(await context.Notes.FindAsync("fleeting01"));

        // Target should be re-queued for embedding
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);
        Assert.Equal("target01", noteId);
    }

    [Fact]
    public async Task MergeNoteAsync_ReturnsNullWhenFleetingNotFound()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "target01", Title = "Target", Content = "C"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.MergeNoteAsync("nonexistent", "target01");

        Assert.Null(result);
    }

    [Fact]
    public async Task MergeNoteAsync_ReturnsNullWhenTargetNotFound()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "fleeting01", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var result = await service.MergeNoteAsync("fleeting01", "nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task MergeNoteAsync_CreatesVersionSnapshotBeforeMerge()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "fleeting01", Title = "F", Content = "Fleeting content",
                Status = NoteStatus.Fleeting
            },
            new Note
            {
                Id = "target01", Title = "Target", Content = "Original",
                Status = NoteStatus.Permanent
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.MergeNoteAsync("fleeting01", "target01");

        var versions = await context.NoteVersions
            .Where(v => v.NoteId == "target01")
            .ToListAsync();
        Assert.Single(versions);
        Assert.Equal("Original", versions[0].Content);
        Assert.Equal("Target", versions[0].Title);
    }

    // ── Feature 5: AI Suggested Tags (InMemory fallback) ───────

    [Fact]
    public async Task GetSuggestedTagsAsync_ReturnsEmptyForNullEmbedding()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "No Embedding", Content = "C",
            Embedding = null
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var tags = await service.GetSuggestedTagsAsync("20260213120001");

        Assert.Empty(tags);
    }

    [Fact]
    public async Task GetSuggestedTagsAsync_ReturnsEmptyForNonexistentNote()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var tags = await service.GetSuggestedTagsAsync("nonexistent");

        Assert.Empty(tags);
    }

    [Fact]
    public async Task GetSuggestedTagsAsync_ReturnsEmptyOnInMemoryProvider()
    {
        // InMemory provider doesn't support raw SQL with pgvector
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Has Embedding", Content = "C",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var tags = await service.GetSuggestedTagsAsync("20260213120001");

        // Should return empty (graceful fallback for InMemory)
        Assert.Empty(tags);
    }

    // ── Feature 6: Duplicate Detection (InMemory fallback) ─────

    [Fact]
    public async Task CheckDuplicateAsync_ReturnsNotDuplicateWithoutEmbeddingGenerator()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var result = await service.CheckDuplicateAsync("Some content");

        Assert.False(result.IsDuplicate);
        Assert.Null(result.SimilarNoteId);
        Assert.Equal(0, result.Similarity);
    }

    [Fact]
    public async Task CheckDuplicateAsync_ReturnsNotDuplicateOnInMemoryFallback()
    {
        await using var context = CreateDbContext();
        var generator = new ZettelWeb.Tests.Fakes.FakeEmbeddingGenerator(
            new float[] { 0.1f, 0.2f });
        var service = new NoteService(context, CreateQueue(), generator);

        var result = await service.CheckDuplicateAsync("Some content");

        // InMemory doesn't support pgvector SQL, falls back to not-duplicate
        Assert.False(result.IsDuplicate);
    }

    // ── Feature 7: Discovery Algorithms ────────────────────────

    [Fact]
    public async Task DiscoveryService_GetRandomForgotten_ReturnsOldNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Old Note", Content = "C",
                UpdatedAt = DateTime.UtcNow.AddDays(-45)
            },
            new Note
            {
                Id = "20260213120002", Title = "Recent Note", Content = "C",
                UpdatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new Note
            {
                Id = "20260213120003", Title = "Ancient Note", Content = "C",
                UpdatedAt = DateTime.UtcNow.AddDays(-60)
            });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetRandomForgottenAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, n => Assert.True(n.UpdatedAt < DateTime.UtcNow.AddDays(-30)));
    }

    [Fact]
    public async Task DiscoveryService_GetRandomForgotten_LimitsToRequestedCount()
    {
        await using var context = CreateDbContext();
        for (var i = 0; i < 10; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"2026021312{i:D004}",
                Title = $"Old {i}",
                Content = "C",
                UpdatedAt = DateTime.UtcNow.AddDays(-45)
            });
        }
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetRandomForgottenAsync(count: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task DiscoveryService_GetRandomForgotten_ReturnsEmptyWhenNoneOld()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Recent", Content = "C",
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetRandomForgottenAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task DiscoveryService_GetOrphans_ReturnsNotesWithNoTagsAndNoLinks()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Orphan", Content = "Just text",
                Tags = new List<NoteTag>()
            },
            new Note
            {
                Id = "20260213120002", Title = "Tagged", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120002", Tag = "rust" }
                }
            },
            new Note
            {
                Id = "20260213120003", Title = "Linked", Content = "See [[Other Note]]",
                Tags = new List<NoteTag>()
            },
            new Note
            {
                Id = "20260213120004", Title = "Also Orphan", Content = "Plain text too",
                Tags = new List<NoteTag>()
            });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetOrphansAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, n => n.Title == "Orphan");
        Assert.Contains(results, n => n.Title == "Also Orphan");
    }

    [Fact]
    public async Task DiscoveryService_GetOrphans_LimitsResults()
    {
        await using var context = CreateDbContext();
        for (var i = 0; i < 10; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"2026021312{i:D004}",
                Title = $"Orphan {i}",
                Content = "Plain text"
            });
        }
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetOrphansAsync(count: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task DiscoveryService_GetThisDayInHistory_ReturnsNotesFromSameDay()
    {
        await using var context = CreateDbContext();
        var today = DateTime.UtcNow;
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Today Note", Content = "C",
                CreatedAt = today
            },
            new Note
            {
                Id = "20260213120002", Title = "Yesterday Note", Content = "C",
                CreatedAt = today.AddDays(-1)
            });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);

        var results = await service.GetThisDayInHistoryAsync();

        Assert.Contains(results, n => n.Title == "Today Note");
        // Only includes notes from this day/month
    }

    [Fact]
    public async Task DiscoveryService_GetThisDayInHistory_ReturnsEmptyWhenNoMatch()
    {
        await using var context = CreateDbContext();
        // Create a note for a different day
        var differentDay = DateTime.UtcNow.AddDays(-15);
        // Only if today's day is different from differentDay's day
        if (differentDay.Day != DateTime.UtcNow.Day ||
            differentDay.Month != DateTime.UtcNow.Month)
        {
            context.Notes.Add(new Note
            {
                Id = "20260213120001", Title = "Other Day", Content = "C",
                CreatedAt = differentDay
            });
            await context.SaveChangesAsync();
        }
        var service = new DiscoveryService(context);

        var results = await service.GetThisDayInHistoryAsync();

        // Should not include notes from a different day
        Assert.DoesNotContain(results, n => n.Title == "Other Day");
    }

    // ── Feature 8: Note Version History ────────────────────────

    [Fact]
    public async Task UpdateAsync_CreatesVersionSnapshot()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Original Title",
            Content = "Original Content",
            Tags = new List<NoteTag>
            {
                new() { NoteId = "20260213120000", Tag = "rust" },
                new() { NoteId = "20260213120000", Tag = "coding" }
            }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.UpdateAsync("20260213120000", "New Title", "New Content");

        var versions = await context.NoteVersions
            .Where(v => v.NoteId == "20260213120000")
            .ToListAsync();
        Assert.Single(versions);
        Assert.Equal("Original Title", versions[0].Title);
        Assert.Equal("Original Content", versions[0].Content);
        Assert.Contains("rust", versions[0].Tags!);
        Assert.Contains("coding", versions[0].Tags!);
    }

    [Fact]
    public async Task UpdateAsync_CreatesMultipleVersions()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "V1", Content = "Content V1"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.UpdateAsync("20260213120000", "V2", "Content V2");
        await service.UpdateAsync("20260213120000", "V3", "Content V3");

        var versions = await context.NoteVersions
            .Where(v => v.NoteId == "20260213120000")
            .OrderBy(v => v.SavedAt)
            .ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal("V1", versions[0].Title);
        Assert.Equal("Content V1", versions[0].Content);
        Assert.Equal("V2", versions[1].Title);
        Assert.Equal("Content V2", versions[1].Content);
    }

    [Fact]
    public async Task GetVersionsAsync_ReturnsVersionsOrderedByDate()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        context.NoteVersions.AddRange(
            new NoteVersion
            {
                NoteId = "20260213120000",
                Title = "Old Title",
                Content = "Old Content",
                SavedAt = DateTime.UtcNow.AddHours(-2)
            },
            new NoteVersion
            {
                NoteId = "20260213120000",
                Title = "Newer Title",
                Content = "Newer Content",
                SavedAt = DateTime.UtcNow.AddHours(-1)
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var versions = await service.GetVersionsAsync("20260213120000");

        Assert.Equal(2, versions.Count);
        // Most recent first
        Assert.Equal("Newer Title", versions[0].Title);
        Assert.Equal("Old Title", versions[1].Title);
    }

    [Fact]
    public async Task GetVersionsAsync_ReturnsEmptyForNoVersions()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var versions = await service.GetVersionsAsync("20260213120000");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsSingleVersion()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        var ver = new NoteVersion
        {
            NoteId = "20260213120000",
            Title = "Version Title",
            Content = "Version Content",
            Tags = "rust,go"
        };
        context.NoteVersions.Add(ver);
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        var version = await service.GetVersionAsync("20260213120000", ver.Id);

        Assert.NotNull(version);
        Assert.Equal("Version Title", version.Title);
        Assert.Equal("Version Content", version.Content);
        Assert.Equal("rust,go", version.Tags);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsNullWhenNotFound()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, CreateQueue());

        var version = await service.GetVersionAsync("nonexistent", 999);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsNullWhenVersionBelongsToDifferentNote()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note { Id = "note1", Title = "T", Content = "C" },
            new Note { Id = "note2", Title = "T", Content = "C" });
        var ver = new NoteVersion
        {
            NoteId = "note1",
            Title = "V",
            Content = "C"
        };
        context.NoteVersions.Add(ver);
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        // Try to get note1's version using note2's ID
        var version = await service.GetVersionAsync("note2", ver.Id);

        Assert.Null(version);
    }

    [Fact]
    public async Task UpdateAsync_VersionHasNullTagsWhenNoteHasNoTags()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "No Tags", Content = "Content"
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, CreateQueue());

        await service.UpdateAsync("20260213120000", "New", "New Content");

        var versions = await context.NoteVersions
            .Where(v => v.NoteId == "20260213120000")
            .ToListAsync();
        Assert.Single(versions);
        Assert.Null(versions[0].Tags);
    }
}
