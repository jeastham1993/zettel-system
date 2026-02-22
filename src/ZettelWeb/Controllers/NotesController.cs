using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Request to create a new note.</summary>
public record CreateNoteRequest(
    [MaxLength(500)] string? Title,
    [Required, MaxLength(500_000)] string Content,
    string[]? Tags = null,
    string? Status = null,
    string? Source = null,
    string? NoteType = null,
    string? SourceAuthor = null,
    string? SourceTitle = null,
    string? SourceUrl = null,
    int? SourceYear = null,
    string? SourceType = null);

/// <summary>Request to update an existing note.</summary>
public record UpdateNoteRequest(
    [Required, MaxLength(500)] string Title,
    [Required, MaxLength(500_000)] string Content,
    string[]? Tags = null,
    string? NoteType = null,
    string? SourceAuthor = null,
    string? SourceTitle = null,
    string? SourceUrl = null,
    int? SourceYear = null,
    string? SourceType = null);

/// <summary>Request to check for duplicate content.</summary>
public record CheckDuplicateRequest(
    [Required, MaxLength(500_000)] string Content);

/// <summary>Response for the inbox count endpoint.</summary>
public record InboxCountResponse(int Count);

/// <summary>Response for the re-embed endpoint.</summary>
public record ReEmbedResponse(int Queued);

/// <summary>Manages notes — CRUD, inbox workflow, versions, backlinks, and discovery.</summary>
[ApiController]
[Route("api/notes")]
[Produces("application/json")]
public class NotesController : ControllerBase
{
    private readonly INoteService _noteService;
    private readonly ISearchService _searchService;

    public NotesController(INoteService noteService, ISearchService searchService)
    {
        _noteService = noteService;
        _searchService = searchService;
    }

    private static NoteType? ParseNoteType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "regular" => NoteType.Regular,
            "structure" => NoteType.Structure,
            "source" => NoteType.Source,
            null => null,
            _ => (NoteType?)(-1) // sentinel for invalid values
        };
    }

    /// <summary>Create a new note. Set status to "fleeting" for inbox notes.</summary>
    /// <remarks>Request body is limited to 1 MB.</remarks>
    [HttpPost]
    [RequestSizeLimit(1_048_576)] // 1 MB
    [ProducesResponseType<Note>(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromBody] CreateNoteRequest request)
    {
        Note note;
        if (string.Equals(request.Status, "fleeting", StringComparison.OrdinalIgnoreCase))
        {
            note = await _noteService.CreateFleetingAsync(
                request.Content, request.Source ?? "web", request.Tags);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { error = "Title is required for permanent notes." });

            var noteType = ParseNoteType(request.NoteType);
            if (noteType == (NoteType)(-1))
                return BadRequest(new { error = $"Invalid noteType: '{request.NoteType}'. Must be regular, structure, or source." });

            note = await _noteService.CreateAsync(
                request.Title, request.Content, request.Tags,
                noteType,
                request.SourceAuthor, request.SourceTitle,
                request.SourceUrl, request.SourceYear, request.SourceType);
        }

        return CreatedAtAction(nameof(GetById), new { id = note.Id }, note);
    }

    /// <summary>Get a note by its ID.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<Note>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(string id)
    {
        var note = await _noteService.GetByIdAsync(id);

        if (note is null)
            return NotFound();

        return Ok(note);
    }

    /// <summary>List notes with optional filtering by status, tag, and note type.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<Note>>(200)]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? tag = null,
        [FromQuery] string? noteType = null)
    {
        NoteStatus? statusFilter = status?.ToLowerInvariant() switch
        {
            "fleeting" => NoteStatus.Fleeting,
            "permanent" => NoteStatus.Permanent,
            _ => null
        };

        var typeFilter = ParseNoteType(noteType);

        var result = await _noteService.ListAsync(skip, take, statusFilter, tag,
            typeFilter is (NoteType)(-1) ? null : typeFilter);

        return Ok(result);
    }

    /// <summary>List fleeting notes in the inbox.</summary>
    [HttpGet("inbox")]
    [ProducesResponseType<PagedResult<Note>>(200)]
    public async Task<IActionResult> ListInbox(
        [FromQuery] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50)
    {
        var result = await _noteService.ListAsync(skip, take, NoteStatus.Fleeting);

        return Ok(result);
    }

    /// <summary>Get the count of fleeting notes in the inbox.</summary>
    [HttpGet("inbox/count")]
    [ProducesResponseType<InboxCountResponse>(200)]
    public async Task<IActionResult> InboxCount()
    {
        var count = await _noteService.CountFleetingAsync();

        return Ok(new InboxCountResponse(count));
    }

    /// <summary>Promote a fleeting note to permanent status.</summary>
    [HttpPost("{id}/promote")]
    [ProducesResponseType<Note>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Promote(string id,
        [FromQuery] string? noteType = null)
    {
        var targetType = ParseNoteType(noteType);

        var note = await _noteService.PromoteAsync(id,
            targetType is (NoteType)(-1) ? null : targetType);

        if (note is null)
            return NotFound();

        return Ok(note);
    }

    /// <summary>Update an existing note.</summary>
    /// <remarks>Request body is limited to 1 MB.</remarks>
    [HttpPut("{id}")]
    [RequestSizeLimit(1_048_576)] // 1 MB
    [ProducesResponseType<Note>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateNoteRequest request)
    {
        var noteType = ParseNoteType(request.NoteType);

        var note = await _noteService.UpdateAsync(
            id, request.Title, request.Content, request.Tags,
            noteType is (NoteType)(-1) ? null : noteType,
            request.SourceAuthor, request.SourceTitle,
            request.SourceUrl, request.SourceYear, request.SourceType);

        if (note is null)
            return NotFound();

        return Ok(note);
    }

    /// <summary>Delete a note by its ID.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _noteService.DeleteAsync(id);

        if (!deleted)
            return NotFound();

        return NoContent();
    }

    /// <summary>Queue all notes for re-embedding.</summary>
    [HttpPost("re-embed")]
    [ProducesResponseType<ReEmbedResponse>(200)]
    public async Task<IActionResult> ReEmbed()
    {
        var count = await _noteService.ReEmbedAllAsync();

        return Ok(new ReEmbedResponse(count));
    }

    /// <summary>Discover unrelated notes using semantic diversity.</summary>
    [HttpGet("discover")]
    [ProducesResponseType<IReadOnlyList<SearchResult>>(200)]
    public async Task<IActionResult> Discover([FromQuery] int limit = 5)
    {
        var results = await _searchService.DiscoverAsync(limit: limit);

        return Ok(results);
    }

    /// <summary>Search note titles for autocomplete.</summary>
    [HttpGet("search-titles")]
    [ProducesResponseType<IReadOnlyList<TitleSearchResult>>(200)]
    public async Task<IActionResult> SearchTitles([FromQuery] string q = "")
    {
        var results = await _noteService.SearchTitlesAsync(q);

        return Ok(results);
    }

    /// <summary>Find semantically related notes.</summary>
    [HttpGet("{id}/related")]
    [ProducesResponseType<IReadOnlyList<SearchResult>>(200)]
    public async Task<IActionResult> Related(string id, [FromQuery] int limit = 5)
    {
        var results = await _searchService.FindRelatedAsync(id, limit);

        return Ok(results);
    }

    /// <summary>Get backlinks — notes that reference this note via [[wiki-links]].</summary>
    [HttpGet("{id}/backlinks")]
    [ProducesResponseType<IReadOnlyList<BacklinkResult>>(200)]
    public async Task<IActionResult> Backlinks(string id)
    {
        var results = await _noteService.GetBacklinksAsync(id);

        return Ok(results);
    }

    /// <summary>Merge a fleeting note into a target permanent note.</summary>
    [HttpPost("{fleetingId}/merge/{targetId}")]
    [ProducesResponseType<Note>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Merge(string fleetingId, string targetId)
    {
        var result = await _noteService.MergeNoteAsync(fleetingId, targetId);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>Get AI-suggested tags for a note.</summary>
    [HttpGet("{id}/suggested-tags")]
    [ProducesResponseType<IReadOnlyList<string>>(200)]
    public async Task<IActionResult> SuggestedTags(string id)
    {
        var tags = await _noteService.GetSuggestedTagsAsync(id);

        return Ok(tags);
    }

    /// <summary>Check if content is a duplicate of an existing note.</summary>
    [HttpPost("check-duplicate")]
    [ProducesResponseType<DuplicateCheckResult>(200)]
    public async Task<IActionResult> CheckDuplicate(
        [FromBody] CheckDuplicateRequest request)
    {
        var result = await _noteService.CheckDuplicateAsync(request.Content);

        return Ok(result);
    }

    /// <summary>Get all versions of a note.</summary>
    [HttpGet("{id}/versions")]
    [ProducesResponseType<IReadOnlyList<NoteVersion>>(200)]
    public async Task<IActionResult> GetVersions(string id)
    {
        var versions = await _noteService.GetVersionsAsync(id);

        return Ok(versions);
    }

    /// <summary>Get a specific version of a note.</summary>
    [HttpGet("{id}/versions/{versionId:int}")]
    [ProducesResponseType<NoteVersion>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetVersion(string id, int versionId)
    {
        var version = await _noteService.GetVersionAsync(id, versionId);

        if (version is null)
            return NotFound();

        return Ok(version);
    }
}

/// <summary>Search and browse tags.</summary>
[ApiController]
[Route("api/tags")]
[Produces("application/json")]
public class TagsController : ControllerBase
{
    private readonly INoteService _noteService;

    public TagsController(INoteService noteService)
    {
        _noteService = noteService;
    }

    /// <summary>Search tags by prefix for autocomplete.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<string>>(200)]
    public async Task<IActionResult> Search([FromQuery] string q = "")
    {
        var tags = await _noteService.SearchTagsAsync(q);

        return Ok(tags);
    }
}
