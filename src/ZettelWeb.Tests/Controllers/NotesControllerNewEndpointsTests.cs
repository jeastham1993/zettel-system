using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Controllers;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

public class NotesControllerNewEndpointsTests
{
    private (NotesController Controller, ZettelDbContext Db) CreateController()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var service = new NoteService(db, new ChannelEmbeddingQueue());
        var controller = new NotesController(service, new FakeSearchService());
        return (controller, db);
    }

    // ── Tag Filtering ──────────────────────────────────────────

    [Fact]
    public async Task List_WithTagFilter_ReturnsFilteredNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Rust", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120001", Tag = "programming" }
                }
            },
            new Note
            {
                Id = "20260213120002", Title = "Cooking", Content = "C",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120002", Tag = "cooking" }
                }
            });
        await db.SaveChangesAsync();

        var result = await controller.List(tag: "programming");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Single(paged.Items);
        Assert.Equal("Rust", paged.Items[0].Title);
    }

    // ── Backlinks ──────────────────────────────────────────────

    [Fact]
    public async Task Backlinks_ReturnsOkWithResults()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note
            {
                Id = "20260213120001", Title = "Target Note", Content = "I'm linked to"
            },
            new Note
            {
                Id = "20260213120002", Title = "Linker",
                Content = "Check [[Target Note]] for info"
            });
        await db.SaveChangesAsync();

        var result = await controller.Backlinks("20260213120001");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var backlinks = Assert.IsAssignableFrom<IReadOnlyList<BacklinkResult>>(okResult.Value);
        Assert.Single(backlinks);
        Assert.Equal("20260213120002", backlinks[0].Id);
    }

    [Fact]
    public async Task Backlinks_ReturnsOkWithEmptyWhenNoBacklinks()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Lonely", Content = "No links"
        });
        await db.SaveChangesAsync();

        var result = await controller.Backlinks("20260213120001");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var backlinks = Assert.IsAssignableFrom<IReadOnlyList<BacklinkResult>>(okResult.Value);
        Assert.Empty(backlinks);
    }

    // ── Merge ──────────────────────────────────────────────────

    [Fact]
    public async Task Merge_ReturnsOkWithMergedNote()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note
            {
                Id = "fleeting01", Title = "F", Content = "Fleeting content",
                Status = NoteStatus.Fleeting
            },
            new Note
            {
                Id = "target01", Title = "Target", Content = "Existing",
                Status = NoteStatus.Permanent
            });
        await db.SaveChangesAsync();

        var result = await controller.Merge("fleeting01", "target01");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var note = Assert.IsType<Note>(okResult.Value);
        Assert.Contains("Existing", note.Content);
        Assert.Contains("Fleeting content", note.Content);
    }

    [Fact]
    public async Task Merge_ReturnsNotFoundWhenFleetingMissing()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "target01", Title = "Target", Content = "C"
        });
        await db.SaveChangesAsync();

        var result = await controller.Merge("nonexistent", "target01");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Merge_ReturnsNotFoundWhenTargetMissing()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "fleeting01", Title = "F", Content = "C",
            Status = NoteStatus.Fleeting
        });
        await db.SaveChangesAsync();

        var result = await controller.Merge("fleeting01", "nonexistent");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Suggested Tags ─────────────────────────────────────────

    [Fact]
    public async Task SuggestedTags_ReturnsOk()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "T", Content = "C"
        });
        await db.SaveChangesAsync();

        var result = await controller.SuggestedTags("20260213120001");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    // ── Check Duplicate ────────────────────────────────────────

    [Fact]
    public async Task CheckDuplicate_ReturnsOk()
    {
        var (controller, _) = CreateController();

        var result = await controller.CheckDuplicate(
            new CheckDuplicateRequest("Some content"));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var dupResult = Assert.IsType<DuplicateCheckResult>(okResult.Value);
        Assert.False(dupResult.IsDuplicate);
    }

    // ── Versions ───────────────────────────────────────────────

    [Fact]
    public async Task GetVersions_ReturnsOkWithVersions()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        db.NoteVersions.Add(new NoteVersion
        {
            NoteId = "20260213120000",
            Title = "Old Title",
            Content = "Old Content"
        });
        await db.SaveChangesAsync();

        var result = await controller.GetVersions("20260213120000");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var versions = Assert.IsAssignableFrom<IReadOnlyList<NoteVersion>>(okResult.Value);
        Assert.Single(versions);
    }

    [Fact]
    public async Task GetVersions_ReturnsEmptyWhenNoVersions()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        await db.SaveChangesAsync();

        var result = await controller.GetVersions("20260213120000");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var versions = Assert.IsAssignableFrom<IReadOnlyList<NoteVersion>>(okResult.Value);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetVersion_ReturnsOkWhenFound()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000", Title = "T", Content = "C"
        });
        var ver = new NoteVersion
        {
            NoteId = "20260213120000",
            Title = "V Title",
            Content = "V Content"
        };
        db.NoteVersions.Add(ver);
        await db.SaveChangesAsync();

        var result = await controller.GetVersion("20260213120000", ver.Id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var version = Assert.IsType<NoteVersion>(okResult.Value);
        Assert.Equal("V Title", version.Title);
    }

    [Fact]
    public async Task GetVersion_ReturnsNotFoundWhenMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.GetVersion("nonexistent", 999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Fleeting Auto-Title via Controller ──────────────────────

    [Fact]
    public async Task Create_Fleeting_WithHtmlContent_StripsHtmlForTitle()
    {
        var (controller, _) = CreateController();

        var request = new CreateNoteRequest(null,
            "<p>Important idea</p><br>Details here",
            Status: "fleeting");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var note = Assert.IsType<Note>(createdResult.Value);
        Assert.Equal("Important idea Details here", note.Title);
        Assert.DoesNotContain("<p>", note.Title);
    }
}
