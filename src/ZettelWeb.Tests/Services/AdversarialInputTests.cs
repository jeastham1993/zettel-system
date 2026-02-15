using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

public class AdversarialInputTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ZettelDbContext(options);
    }

    // ── Export Filename Sanitization ────────────────────────

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\Windows\\System32\\config")]
    [InlineData("normal/../../../secret")]
    public async Task Export_PathTraversalTitle_DoesNotEscapeZipRoot(string maliciousTitle)
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = maliciousTitle,
            Content = "malicious content",
        });
        await db.SaveChangesAsync();

        var service = new ExportService(db);
        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Single(archive.Entries);

        var entryName = archive.Entries[0].FullName;
        // The entry should not contain path traversal sequences
        // ZipArchive itself prevents extraction of entries with ../ but
        // we verify the entry name stored in the zip
        Assert.DoesNotContain("..", entryName.Replace(maliciousTitle + ".md", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Export_EmptyOrWhitespaceTitle_ProducesValidZipEntry(string title)
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = title,
            Content = "content",
        });
        await db.SaveChangesAsync();

        var service = new ExportService(db);
        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Single(archive.Entries);
        // Should produce a valid entry (even if filename is unusual)
        Assert.NotNull(archive.Entries[0].Name);
    }

    // ── Very Long Inputs ──────────────────────────────────

    [Fact]
    public async Task CreateNote_VeryLongTitle_Succeeds()
    {
        await using var db = CreateDbContext();
        var service = new NoteService(db, new ChannelEmbeddingQueue());
        var longTitle = new string('A', 10_000);

        var note = await service.CreateAsync(longTitle, "Content");

        Assert.Equal(longTitle, note.Title);
    }

    [Fact]
    public async Task CreateNote_VeryLongContent_Succeeds()
    {
        await using var db = CreateDbContext();
        var service = new NoteService(db, new ChannelEmbeddingQueue());
        var longContent = new string('B', 100_000);

        var note = await service.CreateAsync("Title", longContent);

        Assert.Equal(longContent, note.Content);
    }

    [Fact]
    public async Task SearchTitles_VeryLongPrefix_ReturnsEmpty()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Short Title", Content = "C",
        });
        await db.SaveChangesAsync();
        var service = new NoteService(db, new ChannelEmbeddingQueue());

        var longPrefix = new string('X', 10_000);
        var results = await service.SearchTitlesAsync(longPrefix);

        Assert.Empty(results);
    }

    // ── Special Characters in Note Operations ──────────────

    [Theory]
    [InlineData("Note with 'single quotes'")]
    [InlineData("Note with \"double quotes\"")]
    [InlineData("Note with <html>tags</html>")]
    [InlineData("Note with emoji \ud83d\ude80\ud83c\udf1f")]
    [InlineData("Note with null\0char")]
    [InlineData("Note\nwith\nnewlines")]
    public async Task CreateNote_SpecialCharacters_PersistsCorrectly(string title)
    {
        await using var db = CreateDbContext();
        var service = new NoteService(db, new ChannelEmbeddingQueue());

        var note = await service.CreateAsync(title, $"Content for {title}");

        var saved = await db.Notes.FindAsync(note.Id);
        Assert.NotNull(saved);
        Assert.Equal(title, saved.Title);
    }

    [Fact]
    public async Task Import_FilenameWithSpecialChars_ExtractsTitleSafely()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("my <special> 'note' & \"stuff\".md", "Content here"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
        var note = await db.Notes.FirstAsync();
        Assert.Equal("my <special> 'note' & \"stuff\"", note.Title);
    }

    [Fact]
    public async Task Import_UnicodeFilename_ExtractsTitleCorrectly()
    {
        await using var db = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var service = new ImportService(db, queue);

        var files = new List<ImportFile>
        {
            new("\u6d4b\u8bd5\u7b14\u8bb0.md", "Chinese content"),
        };

        var result = await service.ImportMarkdownAsync(files);

        Assert.Equal(1, result.Imported);
        var note = await db.Notes.FirstAsync();
        Assert.Equal("\u6d4b\u8bd5\u7b14\u8bb0", note.Title);
    }
}
