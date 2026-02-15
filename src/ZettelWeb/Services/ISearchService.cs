using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> FullTextSearchAsync(string query);
    Task<IReadOnlyList<SearchResult>> SemanticSearchAsync(string query);
    Task<IReadOnlyList<SearchResult>> HybridSearchAsync(string query);
    Task<IReadOnlyList<SearchResult>> FindRelatedAsync(string noteId, int limit = 5);
    Task<IReadOnlyList<SearchResult>> DiscoverAsync(int recentCount = 3, int limit = 5);
}
