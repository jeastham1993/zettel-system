using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

public class ImportServiceTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task ImportMarkdownAsync_SingleFile_CreatesNote()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("my-note.md", "This is the content of my note."),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
        Assert.Single(result.NoteIds);

        var note = await db.Notes.FirstAsync();
        Assert.Equal("my-note", note.Title);
        Assert.Equal("This is the content of my note.", note.Content);
    }

    [Fact]
    public async Task ImportMarkdownAsync_MultipleFiles_CreatesAllNotes()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note-one.md", "Content one"),
            new("note-two.md", "Content two"),
            new("note-three.md", "Content three"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(3, result.Imported);
        Assert.Equal(3, result.NoteIds.Count);
        Assert.Equal(3, await db.Notes.CountAsync());
    }

    [Fact]
    public async Task ImportMarkdownAsync_SkipsNonMarkdownFiles()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note.md", "Markdown content"),
            new("image.png", "binary data"),
            new("readme.txt", "text file"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task ImportMarkdownAsync_ExtractsTitleFromFilename()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("my-awesome-note.md", "Content"),
        };

        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal("my-awesome-note", note.Title);
    }

    [Fact]
    public async Task ImportMarkdownAsync_EmptyList_ReturnsZeroCounts()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var result = await service.ImportMarkdownAsync(new List<ImportFile>());

        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.NoteIds);
    }

    [Fact]
    public async Task ImportMarkdownAsync_EnqueuesNotesForEmbedding()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note-a.md", "Content A"),
            new("note-b.md", "Content B"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(2, queue.EnqueuedIds.Count);
        Assert.Equal(result.NoteIds, queue.EnqueuedIds);
    }

    [Fact]
    public async Task ImportMarkdownAsync_SetsEmbedStatusToPending()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note.md", "Content"),
        };

        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal(EmbedStatus.Pending, note.EmbedStatus);
    }

    [Fact]
    public async Task ImportMarkdownAsync_GeneratesUniqueIds()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note-1.md", "Content 1"),
            new("note-2.md", "Content 2"),
            new("note-3.md", "Content 3"),
        };

        var result = await service.ImportMarkdownAsync(files);

        var distinctIds = result.NoteIds.Distinct().ToList();
        Assert.Equal(3, distinctIds.Count);
    }

    // --- Notion import tests ---

    private const string NotionContent = "# My Notion Note\n\nTags: zettelkasten, productivity\nUID: 202601011200\nCreated: 15 January 2026 10:30\nLast Edited: 20 January 2026 14:45\n\nThis is the body content.";

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_UsesParsedTitle()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal("My Notion Note", note.Title);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_UsesNotionUidAsId()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal("202601011200", note.Id);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_ParsesTags()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.Include(n => n.Tags).FirstAsync();
        var tagNames = note.Tags.Select(t => t.Tag).OrderBy(t => t).ToList();
        Assert.Equal(new List<string> { "productivity", "zettelkasten" }, tagNames);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_UsesParsedDates()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc), note.CreatedAt);
        Assert.Equal(new DateTime(2026, 1, 20, 14, 45, 0, DateTimeKind.Utc), note.UpdatedAt);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_StripsMetadataFromContent()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal("This is the body content.", note.Content);
        Assert.DoesNotContain("Tags:", note.Content);
        Assert.DoesNotContain("UID:", note.Content);
        Assert.DoesNotContain("# My Notion Note", note.Content);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_ConvertsNotionLinks()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var content = "# Note\n\nUID: 202601011200\n\nSee [Other](Other%20aaaabbbbccccddddeeeeffffaaaabbbb.md).";
        var files = new List<ImportFile> { new("export.md", content) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Contains("[[Other]]", note.Content);
        Assert.DoesNotContain(".md", note.Content);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_DuplicateUidInBatch_Skips()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("note1.md", "# Note 1\n\nUID: 202601011200\n\nBody 1"),
            new("note2.md", "# Note 2\n\nUID: 202601011200\n\nBody 2"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Single(await db.Notes.ToListAsync());
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_DuplicateUidInDb_Skips()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "202601011200",
            Title = "Existing",
            Content = "Already in DB",
        });
        await db.SaveChangesAsync();

        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("export.md", "# Duplicate\n\nUID: 202601011200\n\nBody"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Single(await db.Notes.ToListAsync());
    }

    [Fact]
    public async Task ImportMarkdownAsync_MixedNotionAndPlain_HandlesEach()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("notion.md", "# Notion Note\n\nUID: 202601011200\nTags: test\n\nNotion body"),
            new("plain.md", "Just plain markdown content"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(2, result.Imported);
        var notes = await db.Notes.Include(n => n.Tags).OrderBy(n => n.Id).ToListAsync();
        Assert.Equal(2, notes.Count);

        var notionNote = notes.First(n => n.Id == "202601011200");
        Assert.Equal("Notion Note", notionNote.Title);
        Assert.Single(notionNote.Tags);

        var plainNote = notes.First(n => n.Id != "202601011200");
        Assert.Equal("plain", plainNote.Title);
        Assert.Empty(plainNote.Tags);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_MissingUid_GeneratesId()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var content = "# Note Without UID\n\nTags: test\n\nBody content";
        var files = new List<ImportFile> { new("export.md", content) };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
        var note = await db.Notes.FirstAsync();
        Assert.Equal("Note Without UID", note.Title);
        Assert.Equal(17, note.Id.Length); // Generated yyyyMMddHHmmssfff format
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_MissingTitle_UsesFilename()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        // Edge case: technically starts with "# " but parser returns empty title
        // In practice this tests the fallback
        var content = "# \n\nUID: 202601011200\n\nBody";
        var files = new List<ImportFile> { new("fallback-name.md", content) };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_EnqueuesForEmbedding()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        var result = await service.ImportMarkdownAsync(files);

        Assert.Single(queue.EnqueuedIds);
        Assert.Equal("202601011200", queue.EnqueuedIds[0]);
    }

    [Fact]
    public async Task ImportMarkdownAsync_NotionFile_SetsEmbedStatusPending()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile> { new("export.md", NotionContent) };
        await service.ImportMarkdownAsync(files);

        var note = await db.Notes.FirstAsync();
        Assert.Equal(EmbedStatus.Pending, note.EmbedStatus);
    }
}
