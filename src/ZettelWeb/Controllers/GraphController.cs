using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Knowledge graph visualization data.</summary>
[ApiController]
[Route("api/graph")]
[Produces("application/json")]
public class GraphController : ControllerBase
{
    private readonly IGraphService _graphService;

    public GraphController(IGraphService graphService)
    {
        _graphService = graphService;
    }

    /// <summary>Build the knowledge graph with wikilink and semantic edges.</summary>
    /// <param name="threshold">Minimum cosine similarity for semantic edges (0.0–1.0).</param>
    [HttpGet]
    [ProducesResponseType<GraphData>(200)]
    public async Task<IActionResult> GetGraph([FromQuery] double threshold = 0.8)
    {
        var graph = await _graphService.BuildGraphAsync(threshold);
        return Ok(graph);
    }
}
