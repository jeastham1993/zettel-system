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

    // ── Fake ────────────────────────────────────────────────────────────

    private class FakeKbHealthService : IKbHealthService
    {
        public Note? InsertResult { get; init; }

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
    }
}
