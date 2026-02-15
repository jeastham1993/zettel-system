using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Shared fake ISearchService that tracks calls and returns configurable results.
/// Replaces the private StubSearchService in NotesControllerTests and
/// FakeSearchService in SearchControllerTests.
/// </summary>
public class FakeSearchService : ISearchService
{
    private readonly IReadOnlyList<SearchResult> _results;
    public string? LastQuery { get; private set; }
    public string? LastSearchType { get; private set; }
    public int LastLimit { get; private set; }

    public FakeSearchService(IReadOnlyList<SearchResult>? results = null)
    {
        _results = results ?? Array.Empty<SearchResult>();
    }

    public Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(string query)
    {
        LastQuery = query;
        LastSearchType = "fulltext";
        return Task.FromResult(_results);
    }

    public Task<IReadOnlyList<SearchResult>> SemanticSearchAsync(string query)
    {
        LastQuery = query;
        LastSearchType = "semantic";
        return Task.FromResult(_results);
    }

    public Task<IReadOnlyList<SearchResult>> HybridSearchAsync(string query)
    {
        LastQuery = query;
        LastSearchType = "hybrid";
        return Task.FromResult(_results);
    }

    public Task<IReadOnlyList<SearchResult>> FindRelatedAsync(string noteId, int limit = 5)
    {
        LastLimit = limit;
        return Task.FromResult(_results);
    }

    public Task<IReadOnlyList<SearchResult>> DiscoverAsync(int recentCount = 3, int limit = 5)
    {
        return Task.FromResult(_results);
    }
}
