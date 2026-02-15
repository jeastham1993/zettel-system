using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Controllers;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

public class SearchControllerTests
{
    [Fact]
    public async Task Search_ReturnsOkWithResults()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>
        {
            new() { NoteId = "note1", Title = "Found", Snippet = "...match...", Rank = 1.0 },
        });
        var controller = new SearchController(fakeService);

        var result = await controller.Search("test query");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var results = Assert.IsAssignableFrom<IReadOnlyList<SearchResult>>(okResult.Value);
        Assert.Single(results);
        Assert.Equal("note1", results[0].NoteId);
    }

    [Fact]
    public async Task Search_ReturnsOkWithEmptyListWhenNoMatches()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        var result = await controller.Search("nothing");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var results = Assert.IsAssignableFrom<IReadOnlyList<SearchResult>>(okResult.Value);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_PassesQueryToService()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search("my search terms");

        Assert.Equal("my search terms", fakeService.LastQuery);
    }

    [Fact]
    public async Task Search_DefaultsToEmptyQuery()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search();

        Assert.Equal("", fakeService.LastQuery);
    }

    [Fact]
    public async Task Search_DefaultsToHybridType()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search("query");

        Assert.Equal("hybrid", fakeService.LastSearchType);
    }

    [Fact]
    public async Task Search_FullTextTypeCallsFullTextSearch()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search("query", "fulltext");

        Assert.Equal("fulltext", fakeService.LastSearchType);
    }

    [Fact]
    public async Task Search_SemanticTypeCallsSemanticSearch()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search("query", "semantic");

        Assert.Equal("semantic", fakeService.LastSearchType);
    }

    [Fact]
    public async Task Search_HybridTypeCallsHybridSearch()
    {
        var fakeService = new FakeSearchService(new List<SearchResult>());
        var controller = new SearchController(fakeService);

        await controller.Search("query", "hybrid");

        Assert.Equal("hybrid", fakeService.LastSearchType);
    }

}
