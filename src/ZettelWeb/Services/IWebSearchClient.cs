namespace ZettelWeb.Services;

public interface IWebSearchClient
{
    /// <summary>
    /// Search the web using the configured provider.
    /// Returns empty list on failure — research runs degrade gracefully.
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
