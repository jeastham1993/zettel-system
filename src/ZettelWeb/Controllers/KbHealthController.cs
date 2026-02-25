using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Knowledge base health analytics — orphan detection, cluster insights, seed coverage.</summary>
[ApiController]
[Route("api/kb-health")]
[Produces("application/json")]
public class KbHealthController : ControllerBase
{
    private readonly IKbHealthService _kbHealth;

    public KbHealthController(IKbHealthService kbHealth)
    {
        _kbHealth = kbHealth;
    }

    /// <summary>
    /// Full KB health overview: scorecard metrics, recent orphan notes, richest clusters,
    /// and notes never used as generation seeds.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType<KbHealthOverview>(200)]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _kbHealth.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// Top semantically similar notes for a given orphan note.
    /// Used to populate the suggestion panel before inserting a wikilink.
    /// </summary>
    /// <param name="id">The orphan note ID.</param>
    /// <param name="limit">Maximum number of suggestions to return (default 5).</param>
    [HttpGet("orphan/{id}/suggestions")]
    [ProducesResponseType<IReadOnlyList<ConnectionSuggestion>>(200)]
    public async Task<IActionResult> GetSuggestions(string id, [FromQuery] int limit = 5)
    {
        var suggestions = await _kbHealth.GetConnectionSuggestionsAsync(id, limit);
        return Ok(suggestions);
    }

    /// <summary>
    /// Insert a <c>[[TargetTitle]]</c> wikilink into an orphan note's content.
    /// The orphan note's embed status is set to Stale for re-processing.
    /// </summary>
    /// <param name="id">The orphan note ID to modify.</param>
    /// <param name="request">The target note to link to.</param>
    [HttpPost("orphan/{id}/link")]
    [ProducesResponseType<Note>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> AddLink(string id, [FromBody] AddLinkRequest request)
    {
        var note = await _kbHealth.InsertWikilinkAsync(id, request.TargetNoteId);
        if (note is null) return NotFound();
        return Ok(note);
    }

    /// <summary>
    /// All permanent notes whose embedding is not yet completed, ordered by status then newest first.
    /// Includes Failed, Pending, Processing, and Stale notes.
    /// </summary>
    [HttpGet("missing-embeddings")]
    [ProducesResponseType<IReadOnlyList<UnembeddedNote>>(200)]
    public async Task<IActionResult> GetMissingEmbeddings()
    {
        var notes = await _kbHealth.GetNotesWithoutEmbeddingsAsync();
        return Ok(notes);
    }

    /// <summary>
    /// Reset a note's embed status to Pending so the background worker picks it up again.
    /// Clears any previous error and resets the retry counter.
    /// </summary>
    /// <param name="id">The note ID to requeue.</param>
    [HttpPost("missing-embeddings/{id}/requeue")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RequeueEmbedding(string id)
    {
        var count = await _kbHealth.RequeueEmbeddingAsync(id);
        if (count == 0) return NotFound();
        return Ok();
    }

    /// <summary>
    /// All permanent notes whose content exceeds the embedding character limit,
    /// ordered by descending character count.
    /// </summary>
    [HttpGet("large-notes")]
    [ProducesResponseType<IReadOnlyList<LargeNote>>(200)]
    public async Task<IActionResult> GetLargeNotes()
    {
        var notes = await _kbHealth.GetLargeNotesAsync();
        return Ok(notes);
    }

    /// <summary>
    /// Summarize a large note's content using an LLM, replacing the original.
    /// The original content is preserved in version history. The note's embedding
    /// is queued for refresh after summarization.
    /// </summary>
    /// <param name="id">The note ID to summarize.</param>
    [HttpPost("large-notes/{id}/summarize")]
    [ProducesResponseType<SummarizeNoteResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SummarizeNote(string id, CancellationToken cancellationToken)
    {
        var response = await _kbHealth.SummarizeNoteAsync(id, cancellationToken);
        if (response is null) return NotFound();
        return Ok(response);
    }

    /// <summary>
    /// Ask the LLM to suggest how a large note could be split into 2–5 atomic notes.
    /// Read-only — no changes are made until <c>ApplySplit</c> is called.
    /// </summary>
    /// <param name="id">The note ID to generate split suggestions for.</param>
    [HttpPost("large-notes/{id}/split-suggestions")]
    [ProducesResponseType<SplitSuggestion>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSplitSuggestions(string id, CancellationToken cancellationToken)
    {
        var suggestion = await _kbHealth.GetSplitSuggestionsAsync(id, cancellationToken);
        if (suggestion is null) return NotFound();
        return Ok(suggestion);
    }

    /// <summary>
    /// Create new notes from a confirmed split. The original note is preserved untouched.
    /// Each new note is created as a permanent note with <c>EmbedStatus.Pending</c>.
    /// </summary>
    /// <param name="id">The original note ID (preserved after the split).</param>
    /// <param name="request">The confirmed sub-notes to create.</param>
    [HttpPost("large-notes/{id}/apply-split")]
    [ProducesResponseType<ApplySplitResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ApplySplit(
        string id, [FromBody] ApplySplitRequest request, CancellationToken cancellationToken)
    {
        var response = await _kbHealth.ApplySplitAsync(id, request.Notes, cancellationToken);
        if (response is null) return NotFound();
        return Ok(response);
    }
}
