using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryService _discoveryService;

    public DiscoveryController(IDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    [HttpGet]
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
