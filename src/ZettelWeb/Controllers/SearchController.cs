using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Full-text, semantic, and hybrid search across notes.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>Search notes using full-text, semantic, or hybrid mode.</summary>
    /// <param name="q">The search query string.</param>
    /// <param name="type">Search type: "hybrid" (default), "fulltext", or "semantic".</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SearchResult>>(200)]
    public async Task<IActionResult> Search(
        [FromQuery] string q = "",
        [FromQuery] string type = "hybrid")
    {
        var results = type switch
        {
            "fulltext" => await _searchService.FullTextSearchAsync(q),
            "semantic" => await _searchService.SemanticSearchAsync(q),
            _ => await _searchService.HybridSearchAsync(q),
        };

        return Ok(results);
    }
}
