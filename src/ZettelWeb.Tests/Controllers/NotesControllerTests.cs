using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Controllers;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

public class NotesControllerTests
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

    [Fact]
    public async Task Create_ReturnsCreatedAtActionWithNote()
    {
        var (controller, _) = CreateController();

        var request = new CreateNoteRequest("Test Title", "Test Content");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, createdResult.StatusCode);
    }

    [Fact]
    public async Task Create_PersistsNoteInDatabase()
    {
        var (controller, db) = CreateController();

        var request = new CreateNoteRequest("Title", "Content");
        await controller.Create(request);

        Assert.Equal(1, await db.Notes.CountAsync());
    }

    [Fact]
    public async Task GetById_ReturnsOkWithNoteWhenFound()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Find Me",
            Content = "Body"
        });
        await db.SaveChangesAsync();

        var result = await controller.GetById("20260213120000");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFoundWhenMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.GetById("99999999999999");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task List_ReturnsOkWithPagedResult()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Note 1", Content = "Content 1" },
            new Note { Id = "20260213120002", Title = "Note 2", Content = "Content 2" });
        await db.SaveChangesAsync();

        var result = await controller.List();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(2, paged.TotalCount);
    }

    [Fact]
    public async Task Update_ReturnsOkWithUpdatedNote()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Old",
            Content = "Old Content"
        });
        await db.SaveChangesAsync();

        var result = await controller.Update(
            "20260213120000",
            new UpdateNoteRequest("New", "New Content"));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var note = Assert.IsType<Note>(okResult.Value);
        Assert.Equal("New", note.Title);
    }

    [Fact]
    public async Task Update_ReturnsNotFoundWhenMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.Update(
            "99999999999999",
            new UpdateNoteRequest("Title", "Content"));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContentWhenFound()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "To Delete",
            Content = "Content"
        });
        await db.SaveChangesAsync();

        var result = await controller.Delete("20260213120000");

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFoundWhenMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.Delete("99999999999999");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ReEmbed_ReturnsOkWithCount()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "A", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "20260213120002", Title = "B", Content = "C", EmbedStatus = EmbedStatus.Completed });
        await db.SaveChangesAsync();

        var result = await controller.ReEmbed();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        var countProp = response!.GetType().GetProperty("queued");
        Assert.Equal(2, (int)countProp!.GetValue(response)!);
    }

    [Fact]
    public async Task ReEmbed_WithNoNotes_ReturnsZeroCount()
    {
        var (controller, _) = CreateController();

        var result = await controller.ReEmbed();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        var countProp = response!.GetType().GetProperty("queued");
        Assert.Equal(0, (int)countProp!.GetValue(response)!);
    }

    [Fact]
    public async Task Related_ReturnsOkWithResults()
    {
        var searchService = new FakeSearchService(new List<SearchResult>
        {
            new() { NoteId = "related1", Title = "Related", Snippet = "...", Rank = 0.9 },
        });
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var noteService = new NoteService(db, new ChannelEmbeddingQueue());
        var controller = new NotesController(noteService, searchService);

        var result = await controller.Related("source-id");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var results = Assert.IsAssignableFrom<IReadOnlyList<SearchResult>>(okResult.Value);
        Assert.Single(results);
    }

    [Fact]
    public async Task Related_DefaultsToLimit5()
    {
        var searchService = new FakeSearchService();
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var noteService = new NoteService(db, new ChannelEmbeddingQueue());
        var controller = new NotesController(noteService, searchService);

        await controller.Related("source-id");

        Assert.Equal(5, searchService.LastLimit);
    }

    // ── Fleeting Notes / Inbox Tests ────────────────────────────

    [Fact]
    public async Task Create_WithFleetingStatus_CreatesFleetingNote()
    {
        var (controller, db) = CreateController();

        var request = new CreateNoteRequest(null, "Quick thought", Status: "fleeting");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var note = Assert.IsType<Note>(createdResult.Value);
        Assert.Equal(NoteStatus.Fleeting, note.Status);
        Assert.Equal("web", note.Source);
    }

    [Fact]
    public async Task Create_WithFleetingStatusAndSource_UsesProvidedSource()
    {
        var (controller, _) = CreateController();

        var request = new CreateNoteRequest(null, "From telegram",
            Status: "fleeting", Source: "telegram");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var note = Assert.IsType<Note>(createdResult.Value);
        Assert.Equal("telegram", note.Source);
    }

    [Fact]
    public async Task Create_WithoutTitle_ReturnsBadRequestForPermanent()
    {
        var (controller, _) = CreateController();

        var request = new CreateNoteRequest(null, "Content without title");
        var result = await controller.Create(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ListInbox_ReturnsOnlyFleetingNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Permanent", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "Fleeting", Content = "C",
                Status = NoteStatus.Fleeting });
        await db.SaveChangesAsync();

        var result = await controller.ListInbox();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Single(paged.Items);
        Assert.Equal(NoteStatus.Fleeting, paged.Items[0].Status);
    }

    [Fact]
    public async Task InboxCount_ReturnsCountOfFleetingNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "P", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "F1", Content = "C",
                Status = NoteStatus.Fleeting },
            new Note { Id = "20260213120003", Title = "F2", Content = "C",
                Status = NoteStatus.Fleeting });
        await db.SaveChangesAsync();

        var result = await controller.InboxCount();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        var countProp = response!.GetType().GetProperty("count");
        Assert.Equal(2, (int)countProp!.GetValue(response)!);
    }

    [Fact]
    public async Task Promote_SetsStatusToPermanent()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
        });
        await db.SaveChangesAsync();

        var result = await controller.Promote("20260213120001");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var note = Assert.IsType<Note>(okResult.Value);
        Assert.Equal(NoteStatus.Permanent, note.Status);
    }

    [Fact]
    public async Task Promote_ReturnsNotFoundWhenMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.Promote("99999999999999");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task List_WithStatusFilter_FiltersNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "P", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "F", Content = "C",
                Status = NoteStatus.Fleeting });
        await db.SaveChangesAsync();

        var fleetingResult = await controller.List(status: "fleeting");

        var okResult = Assert.IsType<OkObjectResult>(fleetingResult);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Single(paged.Items);
        Assert.Equal(NoteStatus.Fleeting, paged.Items[0].Status);
    }

    [Fact]
    public async Task List_WithNoStatusFilter_ReturnsAllNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "P", Content = "C",
                Status = NoteStatus.Permanent },
            new Note { Id = "20260213120002", Title = "F", Content = "C",
                Status = NoteStatus.Fleeting });
        await db.SaveChangesAsync();

        var result = await controller.List();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(2, paged.TotalCount);
    }

    // ── Note Type Tests ────────────────────────────────────────

    [Fact]
    public async Task Create_WithNoteType_SetsType()
    {
        var (controller, db) = CreateController();

        var request = new CreateNoteRequest("Hub Note", "Links",
            NoteType: "structure");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var note = Assert.IsType<Note>(createdResult.Value);
        Assert.Equal(NoteType.Structure, note.NoteType);
    }

    [Fact]
    public async Task Create_WithSourceMetadata_PersistsFields()
    {
        var (controller, db) = CreateController();

        var request = new CreateNoteRequest("Clean Code", "Notes on the book",
            NoteType: "source",
            SourceAuthor: "Robert C. Martin",
            SourceTitle: "Clean Code",
            SourceUrl: "https://example.com",
            SourceYear: 2008,
            SourceType: "book");
        var result = await controller.Create(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var note = Assert.IsType<Note>(createdResult.Value);
        Assert.Equal(NoteType.Source, note.NoteType);
        Assert.Equal("Robert C. Martin", note.SourceAuthor);
        Assert.Equal(2008, note.SourceYear);
    }

    [Fact]
    public async Task Create_WithInvalidNoteType_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        var request = new CreateNoteRequest("Title", "Content",
            NoteType: "invalid");
        var result = await controller.Create(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Update_WithNoteType_ChangesType()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260215120000", Title = "Note", Content = "C",
            NoteType = NoteType.Regular,
        });
        await db.SaveChangesAsync();

        var result = await controller.Update("20260215120000",
            new UpdateNoteRequest("Note", "C", NoteType: "structure"));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var note = Assert.IsType<Note>(okResult.Value);
        Assert.Equal(NoteType.Structure, note.NoteType);
    }

    [Fact]
    public async Task List_WithNoteTypeFilter_FiltersNotes()
    {
        var (controller, db) = CreateController();
        db.Notes.AddRange(
            new Note { Id = "20260215120001", Title = "Regular", Content = "C",
                NoteType = NoteType.Regular },
            new Note { Id = "20260215120002", Title = "Structure", Content = "C",
                NoteType = NoteType.Structure },
            new Note { Id = "20260215120003", Title = "Source", Content = "C",
                NoteType = NoteType.Source });
        await db.SaveChangesAsync();

        var result = await controller.List(noteType: "structure");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Note>>(okResult.Value);
        Assert.Single(paged.Items);
        Assert.Equal("Structure", paged.Items[0].Title);
    }

    [Fact]
    public async Task Promote_WithTargetType_SetsNoteType()
    {
        var (controller, db) = CreateController();
        db.Notes.Add(new Note
        {
            Id = "20260215120001", Title = "Fleeting", Content = "C",
            Status = NoteStatus.Fleeting,
        });
        await db.SaveChangesAsync();

        var result = await controller.Promote("20260215120001", noteType: "source");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var note = Assert.IsType<Note>(okResult.Value);
        Assert.Equal(NoteStatus.Permanent, note.Status);
        Assert.Equal(NoteType.Source, note.NoteType);
    }
}
