using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ZettelWeb.Controllers;

/// <summary>Response DTO for schedule settings.</summary>
public record ScheduleSettingsResponse(
    bool Enabled,
    string DayOfWeek,
    string TimeOfDay);

/// <summary>Request to update schedule settings.</summary>
public record UpdateScheduleRequest(
    bool Enabled,
    string DayOfWeek,
    string TimeOfDay);

/// <summary>Manages content generation schedule settings.</summary>
[ApiController]
[Route("api/content/schedule")]
[Produces("application/json")]
public class ContentScheduleController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ContentScheduleController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Get the current content generation schedule settings.</summary>
    [HttpGet]
    [ProducesResponseType<ScheduleSettingsResponse>(200)]
    public IActionResult GetSchedule()
    {
        var enabled = _configuration.GetValue<bool>("ContentGeneration:Schedule:Enabled");
        var dayOfWeek = _configuration["ContentGeneration:Schedule:DayOfWeek"] ?? "Monday";
        var timeOfDay = _configuration["ContentGeneration:Schedule:TimeOfDay"] ?? "09:00";

        return Ok(new ScheduleSettingsResponse(enabled, dayOfWeek, timeOfDay));
    }

    /// <summary>Update content generation schedule settings.</summary>
    /// <remarks>
    /// Note: Changes via this endpoint are read-only reflections of appsettings.
    /// To persist schedule changes, update appsettings.json or environment variables.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType<ScheduleSettingsResponse>(200)]
    public IActionResult UpdateSchedule([FromBody] UpdateScheduleRequest request)
    {
        // Schedule configuration is read from appsettings. This endpoint returns
        // the requested values to confirm they were received. In production,
        // schedule changes should be made via configuration (env vars, appsettings).
        return Ok(new ScheduleSettingsResponse(
            request.Enabled, request.DayOfWeek, request.TimeOfDay));
    }
}
