using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ZettelWeb.Controllers;

/// <summary>Schedule settings for a single content type.</summary>
public record ContentTypeScheduleResponse(bool Enabled, string TimeOfDay);

/// <summary>Blog schedule adds a DayOfWeek for its weekly cadence.</summary>
public record BlogScheduleResponse(bool Enabled, string DayOfWeek, string TimeOfDay);

/// <summary>Combined per-type schedule settings response.</summary>
public record ScheduleSettingsResponse(BlogScheduleResponse Blog, ContentTypeScheduleResponse Social);

/// <summary>Request to update the blog schedule.</summary>
public record UpdateBlogScheduleRequest(bool Enabled, string DayOfWeek, string TimeOfDay);

/// <summary>Request to update the social schedule.</summary>
public record UpdateSocialScheduleRequest(bool Enabled, string TimeOfDay);

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

    /// <summary>Get the current per-type content generation schedule settings.</summary>
    [HttpGet]
    [ProducesResponseType<ScheduleSettingsResponse>(200)]
    public IActionResult GetSchedule()
    {
        var blogEnabled = _configuration.GetValue<bool>("ContentGeneration:Schedule:Blog:Enabled");
        var blogDay = _configuration["ContentGeneration:Schedule:Blog:DayOfWeek"] ?? "Monday";
        var blogTime = _configuration["ContentGeneration:Schedule:Blog:TimeOfDay"] ?? "09:00";

        var socialEnabled = _configuration.GetValue<bool>("ContentGeneration:Schedule:Social:Enabled");
        var socialTime = _configuration["ContentGeneration:Schedule:Social:TimeOfDay"] ?? "09:00";

        return Ok(new ScheduleSettingsResponse(
            Blog: new BlogScheduleResponse(blogEnabled, blogDay, blogTime),
            Social: new ContentTypeScheduleResponse(socialEnabled, socialTime)));
    }

    /// <summary>Update blog schedule settings.</summary>
    /// <remarks>
    /// Note: Changes via this endpoint are read-only reflections of appsettings.
    /// To persist schedule changes, update appsettings.json or environment variables.
    /// </remarks>
    [HttpPut("blog")]
    [ProducesResponseType<BlogScheduleResponse>(200)]
    public IActionResult UpdateBlogSchedule([FromBody] UpdateBlogScheduleRequest request)
    {
        return Ok(new BlogScheduleResponse(request.Enabled, request.DayOfWeek, request.TimeOfDay));
    }

    /// <summary>Update social schedule settings.</summary>
    /// <remarks>
    /// Note: Changes via this endpoint are read-only reflections of appsettings.
    /// To persist schedule changes, update appsettings.json or environment variables.
    /// </remarks>
    [HttpPut("social")]
    [ProducesResponseType<ContentTypeScheduleResponse>(200)]
    public IActionResult UpdateSocialSchedule([FromBody] UpdateSocialScheduleRequest request)
    {
        return Ok(new ContentTypeScheduleResponse(request.Enabled, request.TimeOfDay));
    }
}
