using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

public class ExportServiceTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_SingleNote_CreatesZipWithOneFile()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = "My Note",
            Content = "Hello world",
        });
        await db.SaveChangesAsync();

        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Single(archive.Entries);
        Assert.Equal("My Note.md", archive.Entries[0].Name);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_NoteContentIsFileBody()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = "Test",
            Content = "Some markdown content here.",
        });
        await db.SaveChangesAsync();

        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.Entries[0].Open());
        var content = await reader.ReadToEndAsync();

        Assert.Contains("Some markdown content here.", content);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_MultipleNotes_CreatesMultipleFiles()
    {
        await using var db = CreateDbContext();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Note A", Content = "A" },
            new Note { Id = "20260213120002", Title = "Note B", Content = "B" },
            new Note { Id = "20260213120003", Title = "Note C", Content = "C" });
        await db.SaveChangesAsync();

        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Equal(3, archive.Entries.Count);

        var names = archive.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Note A.md", "Note B.md", "Note C.md" }, names);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_EmptyDb_ReturnsValidEmptyZip()
    {
        await using var db = CreateDbContext();
        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_IncludesYamlFrontMatter()
    {
        await using var db = CreateDbContext();
        var note = new Note
        {
            Id = "20260213120001",
            Title = "Tagged Note",
            Content = "Content here",
            CreatedAt = new DateTime(2026, 2, 13, 12, 0, 1, DateTimeKind.Utc),
        };
        note.Tags.Add(new NoteTag { NoteId = note.Id, Tag = "rust" });
        note.Tags.Add(new NoteTag { NoteId = note.Id, Tag = "programming" });
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.Entries[0].Open());
        var content = await reader.ReadToEndAsync();

        Assert.StartsWith("---", content);
        Assert.Contains("tags: [rust, programming]", content);
        Assert.Contains("id: 20260213120001", content);
        Assert.Contains("Content here", content);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_NoteWithNoTags_OmitsTagsFromFrontMatter()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = "No Tags",
            Content = "Content",
        });
        await db.SaveChangesAsync();

        var service = new ExportService(db);

        var zipBytes = await service.ExportAllAsZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.Entries[0].Open());
        var content = await reader.ReadToEndAsync();

        Assert.StartsWith("---", content);
        Assert.DoesNotContain("tags:", content);
        Assert.Contains("Content", content);
    }

    [Fact]
    public async Task ExportAllAsZipAsync_DoesNotTrackEntities()
    {
        await using var db = CreateDbContext();
        db.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = "Tracked Test",
            Content = "Should not be tracked after export",
        });
        await db.SaveChangesAsync();

        // Clear the change tracker so we start fresh
        db.ChangeTracker.Clear();

        var service = new ExportService(db);
        await service.ExportAllAsZipAsync();

        // After export with AsNoTracking, no entities should be tracked
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // ── Import/Export Round-Trip Tests ──────────────────────

    [Fact]
    public async Task RoundTrip_ExportThenImport_PreservesContent()
    {
        // Arrange: create notes and export them
        await using var exportDb = CreateDbContext();
        exportDb.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Note Alpha", Content = "Alpha content body" },
            new Note { Id = "20260213120002", Title = "Note Beta", Content = "Beta content body" });
        await exportDb.SaveChangesAsync();

        var exportService = new ExportService(exportDb);
        var zipBytes = await exportService.ExportAllAsZipAsync();

        // Extract files from zip to build import input
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var importFiles = new List<ImportFile>();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var fileContent = await reader.ReadToEndAsync();
            importFiles.Add(new ImportFile(entry.Name, fileContent));
        }

        // Act: import into a fresh database
        await using var importDb = CreateDbContext();
        var queue = new FakeEmbeddingQueue();
        var importService = new ImportService(importDb, queue);
        var result = await importService.ImportMarkdownAsync(importFiles);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(2, await importDb.Notes.CountAsync());

        var notes = await importDb.Notes.OrderBy(n => n.Title).ToListAsync();

        // Content should contain the original content body
        // (it may also contain YAML front matter as content since
        // import doesn't strip front matter)
        Assert.Contains("Alpha content body", notes[0].Content);
        Assert.Contains("Beta content body", notes[1].Content);
    }
}
