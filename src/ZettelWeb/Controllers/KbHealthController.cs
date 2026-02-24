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
}
