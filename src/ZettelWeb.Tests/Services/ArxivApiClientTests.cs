using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using ZettelWeb.Services;
using ZettelWeb.Tests.Background;

namespace ZettelWeb.Tests.Services;

public class ArxivApiClientTests
{
    private const string SampleAtomXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>http://arxiv.org/abs/2301.12345v1</id>
            <title>  Test Paper Title  </title>
            <summary>This is the abstract text with   extra spaces.</summary>
            <link href="https://arxiv.org/abs/2301.12345" rel="alternate" type="text/html"/>
            <author><name>Alice Smith</name></author>
            <author><name>Bob Jones</name></author>
            <published>2023-01-20T00:00:00Z</published>
          </entry>
        </feed>
        """;

    private static ArxivApiClient CreateClient(
        string? responseXml = null,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseXml ?? SampleAtomXml, statusCode);
        var services = new ServiceCollection();
        services.AddHttpClient("Arxiv")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();

        return new ArxivApiClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            NullLogger<ArxivApiClient>.Instance);
    }

    [Fact]
    public async Task SearchAsync_MapsResultsCorrectly()
    {
        var client = CreateClient();
        var results = await client.SearchAsync("transformers");

        Assert.Single(results);
        Assert.Equal("Test Paper Title", results[0].Title);
        Assert.Equal("This is the abstract text with extra spaces.", results[0].Abstract);
        Assert.Equal("https://arxiv.org/abs/2301.12345", results[0].Url);
        Assert.Equal(2, results[0].Authors.Length);
        Assert.Contains("Alice Smith", results[0].Authors);
        Assert.NotNull(results[0].Published);
    }

    [Fact]
    public async Task SearchAsync_NormalisesArxivUrlToHttps()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>http://arxiv.org/abs/9999.99999v1</id>
                <title>Title</title>
                <summary>Abstract</summary>
                <link href="http://arxiv.org/abs/9999.99999" rel="alternate" type="text/html"/>
                <author><name>Author</name></author>
                <published>2024-01-01T00:00:00Z</published>
              </entry>
            </feed>
            """;
        var client = CreateClient(xml);
        var results = await client.SearchAsync("test");
        Assert.Single(results);
        Assert.StartsWith("https://", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyForEmptyFeed()
    {
        const string emptyXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
            </feed>
            """;
        var client = CreateClient(emptyXml);
        var results = await client.SearchAsync("something obscure");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyOnMalformedXml()
    {
        var client = CreateClient("this is not xml at all");
        var results = await client.SearchAsync("test");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyOnHttpError()
    {
        var client = CreateClient(statusCode: HttpStatusCode.ServiceUnavailable);
        var results = await client.SearchAsync("test");
        Assert.Empty(results);
    }
}
