using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
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
