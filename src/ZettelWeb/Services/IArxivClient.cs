namespace ZettelWeb.Services;

public interface IArxivClient
{
    /// <summary>
    /// Search Arxiv for academic papers matching the query.
    /// Returns empty list on failure — research runs degrade gracefully.
    /// </summary>
    Task<IReadOnlyList<ArxivResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
