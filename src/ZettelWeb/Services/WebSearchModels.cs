namespace ZettelWeb.Services;

public record WebSearchResult(string Title, string Url, string? Snippet);

public record ArxivResult(
    string ArxivId,
    string Title,
    string? Abstract,
    string Url,
    string[] Authors,
    DateTime? Published);
