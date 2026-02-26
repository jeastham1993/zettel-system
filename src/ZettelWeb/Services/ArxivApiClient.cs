using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ZettelWeb.Services;

public class ArxivApiClient : IArxivClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ArxivApiClient> _logger;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly System.Text.RegularExpressions.Regex Whitespace =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    public ArxivApiClient(IHttpClientFactory httpClientFactory, ILogger<ArxivApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArxivResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Arxiv");
            var url = $"https://export.arxiv.org/api/query?search_query=all:{Uri.EscapeDataString(query)}&max_results={maxResults}&sortBy=relevance";

            var xml = await client.GetStringAsync(url, cancellationToken);
            var doc = XDocument.Parse(xml);

            return doc.Descendants(Atom + "entry")
                .Select(e =>
                {
                    var rawId = e.Element(Atom + "id")?.Value ?? "";
                    var title = NormaliseText(e.Element(Atom + "title")?.Value);
                    var abstractText = NormaliseText(e.Element(Atom + "summary")?.Value);
                    var link = e.Elements(Atom + "link")
                        .FirstOrDefault(l => l.Attribute("rel")?.Value == "alternate")
                        ?.Attribute("href")?.Value ?? NormaliseArxivUrl(rawId);
                    var authors = e.Elements(Atom + "author")
                        .Select(a => a.Element(Atom + "name")?.Value ?? "")
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToArray();
                    var publishedStr = e.Element(Atom + "published")?.Value;
                    DateTime? published = DateTime.TryParse(publishedStr, out var dt) ? dt : null;
                    var arxivId = rawId.Split('/').LastOrDefault() ?? rawId;

                    return new ArxivResult(arxivId, title ?? "", abstractText, NormaliseArxivUrl(link), authors, published);
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Title))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Arxiv search failed for query '{Query}' — returning empty results", query);
            return [];
        }
    }

    private static string NormaliseArxivUrl(string url) =>
        url.Replace("http://arxiv.org/abs/", "https://arxiv.org/abs/");

    private static string? NormaliseText(string? text)
    {
        if (text is null) return null;
        return Whitespace.Replace(text.Trim(), " ");
    }
}
