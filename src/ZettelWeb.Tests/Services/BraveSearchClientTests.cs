using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Background;

namespace ZettelWeb.Tests.Services;

public class BraveSearchClientTests
{
    private static BraveSearchClient CreateClient(
        string? responseJson = null,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string apiKey = "test-key")
    {
        var handler = new FakeHttpMessageHandler(responseJson, statusCode);
        var services = new ServiceCollection();
        services.AddHttpClient("BraveSearch")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new ResearchOptions
        {
            BraveSearch = new BraveSearchOptions { ApiKey = apiKey }
        });

        return new BraveSearchClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            options,
            NullLogger<BraveSearchClient>.Instance);
    }

    [Fact]
    public async Task SearchAsync_MapsResultsCorrectly()
    {
        const string json = """
            {
                "web": {
                    "results": [
                        { "title": "Rust Async Guide", "url": "https://example.com/rust", "description": "A comprehensive guide" },
                        { "title": "Tokio Framework", "url": "https://tokio.rs", "description": null }
                    ]
                }
            }
            """;

        var client = CreateClient(json);
        var results = await client.SearchAsync("rust async");

        Assert.Equal(2, results.Count);
        Assert.Equal("Rust Async Guide", results[0].Title);
        Assert.Equal("https://example.com/rust", results[0].Url);
        Assert.Equal("A comprehensive guide", results[0].Snippet);
        Assert.Equal("Tokio Framework", results[1].Title);
        Assert.Null(results[1].Snippet);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenWebKeyMissing()
    {
        const string json = """{ "type": "search", "query": { "original": "rust" } }""";
        var client = CreateClient(json);
        var results = await client.SearchAsync("rust");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyOnHttpError()
    {
        var client = CreateClient(statusCode: HttpStatusCode.TooManyRequests);
        var results = await client.SearchAsync("rust");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenApiKeyNotConfigured()
    {
        var client = CreateClient(apiKey: "");
        var results = await client.SearchAsync("rust");
        Assert.Empty(results);
    }
}
