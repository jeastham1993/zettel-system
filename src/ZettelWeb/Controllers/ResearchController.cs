using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Background;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

[ApiController]
[Route("api/research")]
public class ResearchController : ControllerBase
{
    private readonly IResearchAgentService _researchService;
    private readonly IResearchExecutionQueue _executionQueue;

    public ResearchController(
        IResearchAgentService researchService,
        IResearchExecutionQueue executionQueue)
    {
        _researchService = researchService;
        _executionQueue = executionQueue;
    }

    // POST /api/research/trigger
    [HttpPost("trigger")]
    public async Task<IActionResult> Trigger(
        [FromBody] TriggerResearchRequest request,
        CancellationToken cancellationToken)
    {
        var agenda = await _researchService.TriggerAsync(request.SourceNoteId, cancellationToken);
        return CreatedAtAction(nameof(Trigger), new { id = agenda.Id }, agenda);
    }

    // POST /api/research/agenda/{agendaId}/approve
    [HttpPost("agenda/{agendaId}/approve")]
    public async Task<IActionResult> ApproveAgenda(
        string agendaId,
        [FromBody] ApproveAgendaRequest request,
        CancellationToken cancellationToken)
    {
        // Enqueue for execution by ResearchExecutionBackgroundService.
        // The background service owns a fresh DI scope per job, so the DbContext
        // lifecycle is independent of this request's scope (fixes C1).
        // The single-reader channel ensures at most one run at a time (fixes C3).
        await _executionQueue.EnqueueAsync(
            new ResearchExecutionJob(agendaId, request.BlockedTaskIds ?? []),
            cancellationToken);

        return Accepted();
    }

    // GET /api/research/findings
    [HttpGet("findings")]
    public async Task<IActionResult> GetFindings(CancellationToken cancellationToken)
    {
        var findings = await _researchService.GetPendingFindingsAsync(cancellationToken);
        return Ok(findings);
    }

    // POST /api/research/findings/{findingId}/accept
    [HttpPost("findings/{findingId}/accept")]
    public async Task<IActionResult> AcceptFinding(
        string findingId,
        CancellationToken cancellationToken)
    {
        try
        {
            var note = await _researchService.AcceptFindingAsync(findingId, cancellationToken);
            return CreatedAtAction(nameof(AcceptFinding), new { id = note.Id }, note);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound();
        }
    }

    // POST /api/research/findings/{findingId}/dismiss
    [HttpPost("findings/{findingId}/dismiss")]
    public async Task<IActionResult> DismissFinding(
        string findingId,
        CancellationToken cancellationToken)
    {
        await _researchService.DismissFindingAsync(findingId, cancellationToken);
        return NoContent();
    }
}

public record TriggerResearchRequest(string? SourceNoteId);
public record ApproveAgendaRequest(IReadOnlyList<string>? BlockedTaskIds);
