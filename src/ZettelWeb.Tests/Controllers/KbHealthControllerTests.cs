using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Controllers;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Controllers;

public class KbHealthControllerTests
{
    private static KbHealthController CreateController(IKbHealthService? svc = null)
        => new(svc ?? new FakeKbHealthService());

    // ── GET /api/kb-health/overview ─────────────────────────────────────

    [Fact]
    public async Task GetOverview_Returns200WithOverview()
    {
        var controller = CreateController();

        var result = await controller.GetOverview();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<KbHealthOverview>(ok.Value);
    }

    // ── GET /api/kb-health/orphan/{id}/suggestions ───────────────────────

    [Fact]
    public async Task GetSuggestions_Returns200WithList()
    {
        var controller = CreateController();

        var result = await controller.GetSuggestions("note1");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsAssignableFrom<IReadOnlyList<ConnectionSuggestion>>(ok.Value);
    }

    // ── POST /api/kb-health/orphan/{id}/link ────────────────────────────

    [Fact]
    public async Task AddLink_Returns200WithUpdatedNote()
    {
        var controller = CreateController(new FakeKbHealthService { InsertResult = new Note
        {
            Id = "orphan", Title = "Orphan", Content = "<p>[[Target]]</p>"
        }});

        var result = await controller.AddLink("orphan", new AddLinkRequest("target"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<Note>(ok.Value);
    }

    [Fact]
    public async Task AddLink_Returns404WhenNoteNotFound()
    {
        var controller = CreateController(new FakeKbHealthService { InsertResult = null });

        var result = await controller.AddLink("missing", new AddLinkRequest("target"));

        Assert.IsType<NotFoundResult>(result);
    }

    // ── GET /api/kb-health/missing-embeddings ────────────────────────────

    [Fact]
    public async Task GetMissingEmbeddings_Returns200WithList()
    {
        var controller = CreateController(new FakeKbHealthService
        {
            MissingEmbeddingsResult = new List<UnembeddedNote>
            {
                new("n1", "Pending Note", DateTime.UtcNow, EmbedStatus.Pending, null),
                new("n2", "Failed Note", DateTime.UtcNow, EmbedStatus.Failed, "timeout"),
            }
        });

        var result = await controller.GetMissingEmbeddings();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<UnembeddedNote>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    // ── POST /api/kb-health/missing-embeddings/{id}/requeue ──────────────

    [Fact]
    public async Task RequeueEmbedding_Returns200WhenNoteFound()
    {
        var controller = CreateController(new FakeKbHealthService { RequeueResult = 1 });

        var result = await controller.RequeueEmbedding("n1");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task RequeueEmbedding_Returns404WhenNoteNotFound()
    {
        var controller = CreateController(new FakeKbHealthService { RequeueResult = 0 });

        var result = await controller.RequeueEmbedding("missing");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── GET /api/kb-health/large-notes ───────────────────────────────────

    [Fact]
    public async Task GetLargeNotes_Returns200WithList()
    {
        var controller = CreateController(new FakeKbHealthService
        {
            LargeNotesResult = new List<LargeNote>
            {
                new("n1", "Big Note", DateTime.UtcNow, 5000),
            }
        });

        var result = await controller.GetLargeNotes();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<LargeNote>>(ok.Value);
        Assert.Single(list);
    }

    // ── POST /api/kb-health/large-notes/{id}/summarize ───────────────────

    [Fact]
    public async Task SummarizeNote_Returns200WithResponse()
    {
        var controller = CreateController(new FakeKbHealthService
        {
            SummarizeResult = new SummarizeNoteResponse("n1", 5000, 200, false)
        });

        var result = await controller.SummarizeNote("n1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<SummarizeNoteResponse>(ok.Value);
    }

    [Fact]
    public async Task SummarizeNote_Returns404WhenNoteNotFound()
    {
        var controller = CreateController(new FakeKbHealthService { SummarizeResult = null });

        var result = await controller.SummarizeNote("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── POST /api/kb-health/large-notes/{id}/split-suggestions ──────────

    [Fact]
    public async Task GetSplitSuggestions_Returns200WithSuggestion()
    {
        var controller = CreateController(new FakeKbHealthService
        {
            SplitSuggestionResult = new SplitSuggestion("n1", "Big Note", new[]
            {
                new SuggestedNote("Part A", "Content A"),
                new SuggestedNote("Part B", "Content B"),
            })
        });

        var result = await controller.GetSplitSuggestions("n1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<SplitSuggestion>(ok.Value);
    }

    [Fact]
    public async Task GetSplitSuggestions_Returns404WhenNoteNotFound()
    {
        var controller = CreateController(new FakeKbHealthService { SplitSuggestionResult = null });

        var result = await controller.GetSplitSuggestions("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── POST /api/kb-health/large-notes/{id}/apply-split ─────────────────

    [Fact]
    public async Task ApplySplit_Returns200WithCreatedIds()
    {
        var controller = CreateController(new FakeKbHealthService
        {
            ApplySplitResult = new ApplySplitResponse("n1", new[] { "new1", "new2" })
        });

        var request = new ApplySplitRequest(new[] { new SuggestedNote("A", "C") });
        var result = await controller.ApplySplit("n1", request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<ApplySplitResponse>(ok.Value);
    }

    [Fact]
    public async Task ApplySplit_Returns404WhenNoteNotFound()
    {
        var controller = CreateController(new FakeKbHealthService { ApplySplitResult = null });

        var request = new ApplySplitRequest(new[] { new SuggestedNote("A", "C") });
        var result = await controller.ApplySplit("missing", request, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Fake ────────────────────────────────────────────────────────────

    private class FakeKbHealthService : IKbHealthService
    {
        public Note? InsertResult { get; init; }
        public IReadOnlyList<UnembeddedNote> MissingEmbeddingsResult { get; init; } = Array.Empty<UnembeddedNote>();
        public int RequeueResult { get; init; }
        public IReadOnlyList<LargeNote> LargeNotesResult { get; init; } = Array.Empty<LargeNote>();
        public SummarizeNoteResponse? SummarizeResult { get; init; }
        public SplitSuggestion? SplitSuggestionResult { get; init; }
        public ApplySplitResponse? ApplySplitResult { get; init; }

        public Task<KbHealthOverview> GetOverviewAsync() =>
            Task.FromResult(new KbHealthOverview(
                new KbHealthScorecard(0, 0, 0, 0),
                Array.Empty<UnconnectedNote>(),
                Array.Empty<ClusterSummary>(),
                Array.Empty<UnusedSeedNote>()));

        public Task<IReadOnlyList<ConnectionSuggestion>> GetConnectionSuggestionsAsync(
            string noteId, int limit = 5) =>
            Task.FromResult<IReadOnlyList<ConnectionSuggestion>>(Array.Empty<ConnectionSuggestion>());

        public Task<Note?> InsertWikilinkAsync(string orphanNoteId, string targetNoteId) =>
            Task.FromResult(InsertResult);

        public Task<IReadOnlyList<UnembeddedNote>> GetNotesWithoutEmbeddingsAsync() =>
            Task.FromResult(MissingEmbeddingsResult);

        public Task<int> RequeueEmbeddingAsync(string noteId) =>
            Task.FromResult(RequeueResult);

        public Task<IReadOnlyList<LargeNote>> GetLargeNotesAsync() =>
            Task.FromResult(LargeNotesResult);

        public Task<SummarizeNoteResponse?> SummarizeNoteAsync(
            string noteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SummarizeResult);

        public Task<SplitSuggestion?> GetSplitSuggestionsAsync(
            string noteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SplitSuggestionResult);

        public Task<ApplySplitResponse?> ApplySplitAsync(
            string noteId, IReadOnlyList<SuggestedNote> notes, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApplySplitResult);
    }
}
