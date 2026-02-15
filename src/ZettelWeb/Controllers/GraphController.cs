using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GraphController : ControllerBase
{
    private readonly IGraphService _graphService;

    public GraphController(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [HttpGet]
    public async Task<IActionResult> GetGraph([FromQuery] double threshold = 0.8)
    {
        var graph = await _graphService.BuildGraphAsync(threshold);
        return Ok(graph);
    }
}
