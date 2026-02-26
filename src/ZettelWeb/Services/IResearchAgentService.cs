using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IResearchAgentService
{
    /// <summary>Analyse KB state, generate research agenda, return it for user review.</summary>
    Task<ResearchAgenda> TriggerAsync(string? sourceNoteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute the approved agenda: set blocked tasks, run searches, synthesise findings.
    /// This is a long-running synchronous operation — callers are responsible for scheduling
    /// it asynchronously (e.g. via a BackgroundService channel).
    /// </summary>
    Task ExecuteAgendaAsync(string agendaId, IReadOnlyList<string> blockedTaskIds, CancellationToken cancellationToken = default);

    /// <summary>Get all findings pending review.</summary>
    Task<IReadOnlyList<ResearchFinding>> GetPendingFindingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Accept a finding — creates a fleeting note and marks finding as Accepted.</summary>
    Task<Note> AcceptFindingAsync(string findingId, CancellationToken cancellationToken = default);

    /// <summary>Dismiss a finding.</summary>
    Task DismissFindingAsync(string findingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets any agendas stuck in Executing state — called on service startup
    /// to recover from a crash or process restart.
    /// </summary>
    Task RecoverStuckAgendasAsync(CancellationToken cancellationToken);
}
