using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

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

public record CheckDuplicateRequest(
    [Required, MaxLength(500_000)] string Content);

[ApiController]
[Route("api/[controller]")]
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

    [HttpPost]
    [RequestSizeLimit(1_048_576)] // 1 MB
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

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var note = await _noteService.GetByIdAsync(id);

        if (note is null)
            return NotFound();

        return Ok(note);
    }

    [HttpGet]
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

    [HttpGet("inbox")]
    public async Task<IActionResult> ListInbox(
        [FromQuery] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50)
    {
        var result = await _noteService.ListAsync(skip, take, NoteStatus.Fleeting);

        return Ok(result);
    }

    [HttpGet("inbox/count")]
    public async Task<IActionResult> InboxCount()
    {
        var count = await _noteService.CountFleetingAsync();

        return Ok(new { count });
    }

    [HttpPost("{id}/promote")]
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

    [HttpPut("{id}")]
    [RequestSizeLimit(1_048_576)] // 1 MB
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _noteService.DeleteAsync(id);

        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpPost("re-embed")]
    public async Task<IActionResult> ReEmbed()
    {
        var count = await _noteService.ReEmbedAllAsync();

        return Ok(new { queued = count });
    }

    [HttpGet("discover")]
    public async Task<IActionResult> Discover([FromQuery] int limit = 5)
    {
        var results = await _searchService.DiscoverAsync(limit: limit);

        return Ok(results);
    }

    [HttpGet("search-titles")]
    public async Task<IActionResult> SearchTitles([FromQuery] string q = "")
    {
        var results = await _noteService.SearchTitlesAsync(q);

        return Ok(results);
    }

    [HttpGet("{id}/related")]
    public async Task<IActionResult> Related(string id, [FromQuery] int limit = 5)
    {
        var results = await _searchService.FindRelatedAsync(id, limit);

        return Ok(results);
    }

    [HttpGet("{id}/backlinks")]
    public async Task<IActionResult> Backlinks(string id)
    {
        var results = await _noteService.GetBacklinksAsync(id);

        return Ok(results);
    }

    [HttpPost("{fleetingId}/merge/{targetId}")]
    public async Task<IActionResult> Merge(string fleetingId, string targetId)
    {
        var result = await _noteService.MergeNoteAsync(fleetingId, targetId);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    [HttpGet("{id}/suggested-tags")]
    public async Task<IActionResult> SuggestedTags(string id)
    {
        var tags = await _noteService.GetSuggestedTagsAsync(id);

        return Ok(tags);
    }

    [HttpPost("check-duplicate")]
    public async Task<IActionResult> CheckDuplicate(
        [FromBody] CheckDuplicateRequest request)
    {
        var result = await _noteService.CheckDuplicateAsync(request.Content);

        return Ok(result);
    }

    [HttpGet("{id}/versions")]
    public async Task<IActionResult> GetVersions(string id)
    {
        var versions = await _noteService.GetVersionsAsync(id);

        return Ok(versions);
    }

    [HttpGet("{id}/versions/{versionId:int}")]
    public async Task<IActionResult> GetVersion(string id, int versionId)
    {
        var version = await _noteService.GetVersionAsync(id, versionId);

        if (version is null)
            return NotFound();

        return Ok(version);
    }
}

[ApiController]
[Route("api/[controller]")]
public class TagsController : ControllerBase
{
    private readonly INoteService _noteService;

    public TagsController(INoteService noteService)
    {
        _noteService = noteService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q = "")
    {
        var tags = await _noteService.SearchTagsAsync(q);

        return Ok(tags);
    }
}
