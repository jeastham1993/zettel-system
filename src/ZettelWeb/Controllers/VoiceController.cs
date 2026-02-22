using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Controllers;

/// <summary>Request to create a new voice example.</summary>
public record CreateVoiceExampleRequest(
    [Required] string Medium,
    string? Title,
    [Required] string Content,
    string? Source);

/// <summary>Request to update voice style configuration.</summary>
public record UpdateVoiceConfigRequest(
    [Required] string Medium,
    string? StyleNotes);

/// <summary>Response DTO for a voice example.</summary>
public record VoiceExampleResponse(
    string Id,
    string Medium,
    string? Title,
    string Content,
    string? Source,
    DateTime CreatedAt);

/// <summary>Response DTO for voice configuration.</summary>
public record VoiceConfigResponse(
    string Id,
    string Medium,
    string? StyleNotes,
    DateTime UpdatedAt);

/// <summary>Manages voice examples and style configuration for content generation.</summary>
[ApiController]
[Route("api/content/voice")]
[Produces("application/json")]
public class VoiceController : ControllerBase
{
    private readonly ZettelDbContext _db;

    public VoiceController(ZettelDbContext db)
    {
        _db = db;
    }

    /// <summary>List all voice examples.</summary>
    [HttpGet("examples")]
    [ProducesResponseType<IReadOnlyList<VoiceExampleResponse>>(200)]
    public async Task<IActionResult> ListExamples()
    {
        var examples = await _db.VoiceExamples
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Ok(examples.Select(MapExample).ToList());
    }

    /// <summary>Add a voice example.</summary>
    [HttpPost("examples")]
    [ProducesResponseType<VoiceExampleResponse>(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateExample([FromBody] CreateVoiceExampleRequest request)
    {
        var example = new VoiceExample
        {
            Id = GenerateId(),
            Medium = request.Medium,
            Title = request.Title,
            Content = request.Content,
            Source = request.Source,
            CreatedAt = DateTime.UtcNow,
        };

        _db.VoiceExamples.Add(example);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(ListExamples), null, MapExample(example));
    }

    /// <summary>Delete a voice example.</summary>
    [HttpDelete("examples/{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteExample(string id)
    {
        var example = await _db.VoiceExamples.FindAsync(id);
        if (example is null)
            return NotFound();

        _db.VoiceExamples.Remove(example);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Get voice configuration, optionally filtered by medium.</summary>
    [HttpGet("config")]
    [ProducesResponseType<IReadOnlyList<VoiceConfigResponse>>(200)]
    public async Task<IActionResult> GetConfig([FromQuery] string? medium = null)
    {
        var query = _db.VoiceConfigs.AsNoTracking();

        if (!string.IsNullOrEmpty(medium))
            query = query.Where(c => c.Medium == medium);

        var configs = await query.ToListAsync();

        return Ok(configs.Select(MapConfig).ToList());
    }

    /// <summary>Create or update voice style notes for a medium.</summary>
    [HttpPut("config")]
    [ProducesResponseType<VoiceConfigResponse>(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateVoiceConfigRequest request)
    {
        var config = await _db.VoiceConfigs
            .FirstOrDefaultAsync(c => c.Medium == request.Medium);

        if (config is null)
        {
            config = new VoiceConfig
            {
                Id = GenerateId(),
                Medium = request.Medium,
                StyleNotes = request.StyleNotes,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.VoiceConfigs.Add(config);
        }
        else
        {
            config.StyleNotes = request.StyleNotes;
            config.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return Ok(MapConfig(config));
    }

    private static VoiceExampleResponse MapExample(VoiceExample e) =>
        new(e.Id, e.Medium, e.Title, e.Content, e.Source, e.CreatedAt);

    private static VoiceConfigResponse MapConfig(VoiceConfig c) =>
        new(c.Id, c.Medium, c.StyleNotes, c.UpdatedAt);

    private static string GenerateId()
    {
        var now = DateTime.UtcNow;
        return $"{now:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 9999)}";
    }
}
