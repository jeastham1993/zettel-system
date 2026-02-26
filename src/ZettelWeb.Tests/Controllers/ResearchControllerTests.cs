using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Controllers;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Controllers;

public class ResearchControllerTests
{
    private static ResearchController CreateController(IResearchAgentService? svc = null)
        => new(svc ?? new FakeResearchAgentService(), new ChannelResearchExecutionQueue());

    [Fact]
    public async Task Trigger_Returns201WithAgenda()
    {
        var controller = CreateController();
        var result = await controller.Trigger(new TriggerResearchRequest(null), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.IsType<ResearchAgenda>(created.Value);
    }

    [Fact]
    public async Task ApproveAgenda_Returns202AndEnqueuesJob()
    {
        var queue = new ChannelResearchExecutionQueue();
        var controller = new ResearchController(new FakeResearchAgentService(), queue);
        var result = await controller.ApproveAgenda("agenda1", new ApproveAgendaRequest([]), CancellationToken.None);
        Assert.IsType<AcceptedResult>(result);

        // Verify the job was enqueued (not executed inline)
        Assert.True(queue.Reader.TryRead(out var job));
        Assert.Equal("agenda1", job.AgendaId);
    }

    [Fact]
    public async Task GetFindings_Returns200WithList()
    {
        var controller = CreateController();
        var result = await controller.GetFindings(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsAssignableFrom<IReadOnlyList<ResearchFinding>>(ok.Value);
    }

    [Fact]
    public async Task AcceptFinding_Returns201WithNote()
    {
        var svc = new FakeResearchAgentService
        {
            AcceptResult = new Note { Id = "n1", Title = "Finding Note", Content = "Synthesis text" }
        };
        var controller = CreateController(svc);
        var result = await controller.AcceptFinding("f1", CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.IsType<Note>(created.Value);
    }

    [Fact]
    public async Task DismissFinding_Returns204()
    {
        var controller = CreateController();
        var result = await controller.DismissFinding("f1", CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

public class FakeResearchAgentService : IResearchAgentService
{
    public Note? AcceptResult { get; set; }

    public Task<ResearchAgenda> TriggerAsync(string? sourceNoteId, CancellationToken ct = default)
        => Task.FromResult(new ResearchAgenda { Id = "agenda1" });

    public Task ExecuteAgendaAsync(string agendaId, IReadOnlyList<string> blockedTaskIds, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ResearchFinding>> GetPendingFindingsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResearchFinding>>(new List<ResearchFinding>());

    public Task<Note> AcceptFindingAsync(string findingId, CancellationToken ct = default)
    {
        if (AcceptResult is null)
            throw new InvalidOperationException("Finding not found");
        return Task.FromResult(AcceptResult);
    }

    public Task DismissFindingAsync(string findingId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RecoverStuckAgendasAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
