using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Discover forgotten, orphaned, or historically relevant notes.</summary>
[ApiController]
[Route("api/discovery")]
[Produces("application/json")]
public class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryService _discoveryService;

    public DiscoveryController(IDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    /// <summary>Discover notes by mode: random forgotten notes, orphans, or this day in history.</summary>
    /// <param name="mode">Discovery mode: "random" (default), "orphans", or "today".</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<Note>>(200)]
    public async Task<IActionResult> Discover([FromQuery] string mode = "random")
    {
        var results = mode.ToLowerInvariant() switch
        {
            "random" => await _discoveryService.GetRandomForgottenAsync(),
            "orphans" => await _discoveryService.GetOrphansAsync(),
            "today" => await _discoveryService.GetThisDayInHistoryAsync(),
            _ => await _discoveryService.GetRandomForgottenAsync(),
        };

        return Ok(results);
    }
}
