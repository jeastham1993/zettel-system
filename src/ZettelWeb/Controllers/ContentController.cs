using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Response DTO for a content generation run.</summary>
public record ContentGenerationResponse(
    string Id,
    string SeedNoteId,
    List<string> ClusterNoteIds,
    string TopicSummary,
    GenerationStatus Status,
    DateTime GeneratedAt,
    DateTime? ReviewedAt);

/// <summary>Response DTO for a content generation run with its pieces.</summary>
public record GenerationWithPiecesResponse(
    string Id,
    string SeedNoteId,
    List<string> ClusterNoteIds,
    string TopicSummary,
    GenerationStatus Status,
    DateTime GeneratedAt,
    DateTime? ReviewedAt,
    List<ContentPieceResponse> Pieces);

/// <summary>Response DTO for a content piece.</summary>
public record ContentPieceResponse(
    string Id,
    string GenerationId,
    string Medium,
    string? Title,
    string Body,
    ContentPieceStatus Status,
    int Sequence,
    DateTime CreatedAt,
    DateTime? ApprovedAt);

/// <summary>Manages generated content — generation, review, approval, and export.</summary>
[ApiController]
[Route("api/content")]
[Produces("application/json")]
public partial class ContentController : ControllerBase
{
    private readonly ZettelDbContext _db;
    private readonly ITopicDiscoveryService _topicDiscovery;
    private readonly IContentGenerationService _contentGeneration;

    public ContentController(
        ZettelDbContext db,
        ITopicDiscoveryService topicDiscovery,
        IContentGenerationService contentGeneration)
    {
        _db = db;
        _topicDiscovery = topicDiscovery;
        _contentGeneration = contentGeneration;
    }

    /// <summary>Trigger a manual content generation run.</summary>
    [HttpPost("generate")]
    [ProducesResponseType<ContentGenerationResponse>(201)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Generate()
    {
        var cluster = await _topicDiscovery.DiscoverTopicAsync();
        if (cluster is null)
            return Conflict(new { error = "No eligible notes available for content generation." });

        var generation = await _contentGeneration.GenerateContentAsync(cluster);

        return CreatedAtAction(
            nameof(GetGeneration),
            new { id = generation.Id },
            MapGeneration(generation));
    }

    /// <summary>Regenerate all content for an existing generation using the same note cluster.</summary>
    [HttpPost("generations/{id}/regenerate")]
    [ProducesResponseType<ContentGenerationResponse>(201)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RegenerateGeneration(string id)
    {
        var existing = await _db.ContentGenerations
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);

        if (existing is null)
            return NotFound();

        if (existing.Status == GenerationStatus.Approved)
            return Conflict(new { error = "Cannot regenerate an approved generation." });

        var notes = await _db.Notes
            .AsNoTracking()
            .Where(n => existing.ClusterNoteIds.Contains(n.Id))
            .ToListAsync();

        if (notes.Count == 0)
            return Conflict(new { error = "None of the original cluster notes exist any longer." });

        var cluster = new TopicCluster(existing.SeedNoteId, notes, existing.TopicSummary);
        var generation = await _contentGeneration.GenerateContentAsync(cluster);

        return CreatedAtAction(
            nameof(GetGeneration),
            new { id = generation.Id },
            MapGeneration(generation));
    }

    /// <summary>Regenerate content pieces for a single medium on an existing generation.</summary>
    [HttpPost("generations/{id}/regenerate/{medium}")]
    [ProducesResponseType<List<ContentPieceResponse>>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RegenerateMedium(string id, string medium)
    {
        if (medium is not ("blog" or "social"))
            return BadRequest(new { error = "medium must be 'blog' or 'social'." });

        var generation = await _db.ContentGenerations
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);

        if (generation is null)
            return NotFound();

        if (generation.Status == GenerationStatus.Approved)
            return Conflict(new { error = "Cannot regenerate an approved generation." });

        var notes = await _db.Notes
            .AsNoTracking()
            .Where(n => generation.ClusterNoteIds.Contains(n.Id))
            .ToListAsync();

        if (notes.Count == 0)
            return Conflict(new { error = "None of the original cluster notes exist any longer." });

        var newPieces = await _contentGeneration.RegenerateMediumAsync(generation, notes, medium);

        return Ok(newPieces.Select(MapPiece).ToList());
    }

    /// <summary>List content generation runs with pagination.</summary>
    [HttpGet("generations")]
    [ProducesResponseType<PagedResult<ContentGenerationResponse>>(200)]
    public async Task<IActionResult> ListGenerations(
        [FromQuery] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50)
    {
        var query = _db.ContentGenerations.AsNoTracking();
        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(g => g.GeneratedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var result = new PagedResult<ContentGenerationResponse>(
            items.Select(MapGeneration).ToList(), totalCount);

        return Ok(result);
    }

    /// <summary>Get a content generation run with its content pieces.</summary>
    [HttpGet("generations/{id}")]
    [ProducesResponseType<GenerationWithPiecesResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetGeneration(string id)
    {
        var generation = await _db.ContentGenerations
            .AsNoTracking()
            .Include(g => g.Pieces.OrderBy(p => p.Sequence))
            .FirstOrDefaultAsync(g => g.Id == id);

        if (generation is null)
            return NotFound();

        return Ok(MapGenerationWithPieces(generation));
    }

    /// <summary>List content pieces with optional filtering.</summary>
    [HttpGet("pieces")]
    [ProducesResponseType<PagedResult<ContentPieceResponse>>(200)]
    public async Task<IActionResult> ListPieces(
        [FromQuery] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        [FromQuery] string? medium = null,
        [FromQuery] string? status = null)
    {
        var query = _db.ContentPieces.AsNoTracking();

        if (!string.IsNullOrEmpty(medium))
            query = query.Where(p => p.Medium == medium);

        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<ContentPieceStatus>(status, ignoreCase: true, out var statusFilter))
                query = query.Where(p => p.Status == statusFilter);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var result = new PagedResult<ContentPieceResponse>(
            items.Select(MapPiece).ToList(), totalCount);

        return Ok(result);
    }

    /// <summary>Get a single content piece.</summary>
    [HttpGet("pieces/{id}")]
    [ProducesResponseType<ContentPieceResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetPiece(string id)
    {
        var piece = await _db.ContentPieces
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (piece is null)
            return NotFound();

        return Ok(MapPiece(piece));
    }

    /// <summary>Approve a content piece for publishing.</summary>
    [HttpPut("pieces/{id}/approve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ApprovePiece(string id)
    {
        var piece = await _db.ContentPieces.FindAsync(id);
        if (piece is null)
            return NotFound();

        piece.Status = ContentPieceStatus.Approved;
        piece.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Reject a content piece.</summary>
    [HttpPut("pieces/{id}/reject")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RejectPiece(string id)
    {
        var piece = await _db.ContentPieces.FindAsync(id);
        if (piece is null)
            return NotFound();

        piece.Status = ContentPieceStatus.Rejected;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Export a content piece as a markdown file.</summary>
    [HttpGet("pieces/{id}/export")]
    [Produces("text/markdown")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExportPiece(string id)
    {
        var piece = await _db.ContentPieces
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (piece is null)
            return NotFound();

        var markdown = piece.Title is not null
            ? $"# {piece.Title}\n\n{piece.Body}"
            : piece.Body;

        var filename = piece.Title is not null
            ? SanitizeFilename(piece.Title) + ".md"
            : $"content-{piece.Id}.md";

        return File(
            System.Text.Encoding.UTF8.GetBytes(markdown),
            "text/markdown",
            filename);
    }

    private static string SanitizeFilename(string title)
    {
        var sanitized = InvalidFilenameCharsRegex().Replace(title, "-");
        sanitized = sanitized.Trim('-');
        if (sanitized.Length > 100) sanitized = sanitized[..100];
        return string.IsNullOrEmpty(sanitized) ? "content" : sanitized;
    }

    private static ContentGenerationResponse MapGeneration(ContentGeneration g) =>
        new(g.Id, g.SeedNoteId, g.ClusterNoteIds, g.TopicSummary,
            g.Status, g.GeneratedAt, g.ReviewedAt);

    private static GenerationWithPiecesResponse MapGenerationWithPieces(ContentGeneration g) =>
        new(g.Id, g.SeedNoteId, g.ClusterNoteIds, g.TopicSummary,
            g.Status, g.GeneratedAt, g.ReviewedAt,
            g.Pieces.Select(MapPiece).ToList());

    private static ContentPieceResponse MapPiece(ContentPiece p) =>
        new(p.Id, p.GenerationId, p.Medium, p.Title, p.Body,
            p.Status, p.Sequence, p.CreatedAt, p.ApprovedAt);

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex InvalidFilenameCharsRegex();
}
