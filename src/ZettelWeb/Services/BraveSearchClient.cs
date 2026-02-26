using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class BraveSearchClient : IWebSearchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<BraveSearchClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BraveSearchClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        ILogger<BraveSearchClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = options.Value.BraveSearch.ApiKey;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Brave Search API key not configured — skipping web search");
            return [];
        }

        try
        {
            var client = _httpClientFactory.CreateClient("BraveSearch");
            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={maxResults}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", _apiKey);
            request.Headers.Add("Accept", "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var results))
                return [];

            var list = new List<WebSearchResult>();
            foreach (var item in results.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url2 = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("description", out var d) ? d.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url2))
                    list.Add(new WebSearchResult(title, url2, snippet));
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brave Search failed for query '{Query}' — returning empty results", query);
            return [];
        }
    }
}
