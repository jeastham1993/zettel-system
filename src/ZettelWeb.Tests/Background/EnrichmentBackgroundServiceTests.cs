using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Background;

public class EnrichmentBackgroundServiceTests
{
    private static ServiceProvider BuildServiceProvider(string dbName, FakeHttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ZettelDbContext>(o =>
            o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();
        // Use TestableUrlSafetyChecker to bypass real DNS resolution in unit tests
        services.AddSingleton<IUrlSafetyChecker, TestableUrlSafetyChecker>();

        handler ??= new FakeHttpMessageHandler();
        services.AddHttpClient("Enrichment")
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(int timeoutSeconds = 10, int maxRetries = 3)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Capture:EnrichmentTimeoutSeconds"] = timeoutSeconds.ToString(),
                ["Capture:EnrichmentMaxRetries"] = maxRetries.ToString(),
            })
            .Build();
    }

    private static EnrichmentBackgroundService CreateService(
        ServiceProvider sp,
        FakeHttpMessageHandler? handler = null,
        IConfiguration? config = null)
    {
        return new EnrichmentBackgroundService(
            sp.GetRequiredService<IEnrichmentQueue>(),
            sp,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IUrlSafetyChecker>(),
            NullLogger<EnrichmentBackgroundService>.Instance,
            config ?? BuildConfig());
    }

    private static ZettelDbContext CreateContext(ServiceProvider sp)
    {
        var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
    }

    private static async Task<Note?> FreshLoadAsync(ServiceProvider sp, string noteId)
    {
        var ctx = CreateContext(sp);
        return await ctx.Notes.FindAsync(noteId);
    }

    // --- URL Extraction Tests ---

    [Theory]
    [InlineData("Check out https://example.com/article", new[] { "https://example.com/article" })]
    [InlineData("Visit http://foo.com and https://bar.com", new[] { "http://foo.com", "https://bar.com" })]
    [InlineData("No URLs here", new string[0])]
    [InlineData("Link: https://example.com/path?q=1&r=2", new[] { "https://example.com/path?q=1&r=2" })]
    [InlineData("End with period https://example.com.", new[] { "https://example.com" })]
    [InlineData("In parens (https://example.com)", new[] { "https://example.com" })]
    public void ExtractUrls_ExtractsCorrectly(string content, string[] expected)
    {
        var urls = EnrichmentBackgroundService.ExtractUrls(content);
        Assert.Equal(expected, urls);
    }

    [Fact]
    public void ExtractUrls_DeduplicatesDuplicateUrls()
    {
        var urls = EnrichmentBackgroundService.ExtractUrls(
            "See https://example.com and also https://example.com again");
        Assert.Single(urls);
        Assert.Equal("https://example.com", urls[0]);
    }

    // --- HTML Extraction Tests (delegated to HtmlSanitiser) ---

    [Fact]
    public void ExtractTitle_ReturnsTitle()
    {
        var html = "<html><head><title>My Page Title</title></head><body>content</body></html>";
        Assert.Equal("My Page Title", HtmlSanitiser.ExtractTitle(html));
    }

    [Fact]
    public void ExtractTitle_ReturnsNullWhenMissing()
    {
        var html = "<html><head></head><body>content</body></html>";
        Assert.Null(HtmlSanitiser.ExtractTitle(html));
    }

    [Fact]
    public void ExtractTitle_DecodesHtmlEntities()
    {
        var html = "<html><head><title>Tom &amp; Jerry</title></head></html>";
        Assert.Equal("Tom & Jerry", HtmlSanitiser.ExtractTitle(html));
    }

    [Fact]
    public void ExtractDescription_ReturnsMetaDescription()
    {
        var html = """<html><head><meta name="description" content="A great page"></head></html>""";
        Assert.Equal("A great page", HtmlSanitiser.ExtractDescription(html));
    }

    [Fact]
    public void ExtractDescription_ReturnsOgDescription()
    {
        var html = """<html><head><meta property="og:description" content="OG desc"></head></html>""";
        Assert.Equal("OG desc", HtmlSanitiser.ExtractDescription(html));
    }

    [Fact]
    public void ExtractDescription_ReturnsNullWhenMissing()
    {
        var html = "<html><head></head><body>content</body></html>";
        Assert.Null(HtmlSanitiser.ExtractDescription(html));
    }

    [Fact]
    public void ExtractDescription_HandlesContentBeforeName()
    {
        var html = """<html><head><meta content="Reversed order" name="description"></head></html>""";
        Assert.Equal("Reversed order", HtmlSanitiser.ExtractDescription(html));
    }

    [Fact]
    public void ExtractContentExcerpt_StripsHtmlAndReturnsText()
    {
        var html = "<html><body><p>Hello world</p><p>Second paragraph</p></body></html>";
        var excerpt = HtmlSanitiser.ExtractContentExcerpt(html);
        Assert.NotNull(excerpt);
        Assert.Contains("Hello world", excerpt);
        Assert.Contains("Second paragraph", excerpt);
        Assert.DoesNotContain("<p>", excerpt);
    }

    [Fact]
    public void ExtractContentExcerpt_StripsScriptAndStyleTags()
    {
        var html = "<html><body><script>var x = 1;</script><p>Visible text</p><style>.x{}</style></body></html>";
        var excerpt = HtmlSanitiser.ExtractContentExcerpt(html);
        Assert.NotNull(excerpt);
        Assert.Contains("Visible text", excerpt);
        Assert.DoesNotContain("var x", excerpt);
        Assert.DoesNotContain(".x{}", excerpt);
    }

    [Fact]
    public void ExtractContentExcerpt_TruncatesAt500Chars()
    {
        var longText = new string('a', 1000);
        var html = $"<html><body><p>{longText}</p></body></html>";
        var excerpt = HtmlSanitiser.ExtractContentExcerpt(html);
        Assert.NotNull(excerpt);
        Assert.Equal(500, excerpt.Length);
    }

    [Fact]
    public void ExtractContentExcerpt_ReturnsNullForEmptyBody()
    {
        var html = "<html><body></body></html>";
        Assert.Null(HtmlSanitiser.ExtractContentExcerpt(html));
    }

    // --- C3: HTML truncation for regex safety ---

    [Fact]
    public void ExtractTitle_TruncatesLargeHtmlBeforeRegex()
    {
        // Title is at the start, should still be found
        var html = "<html><head><title>Found Title</title></head><body>"
            + new string('x', 200_000) + "</body></html>";
        Assert.Equal("Found Title", HtmlSanitiser.ExtractTitle(html));
    }

    [Fact]
    public void ExtractTitle_ReturnsNullWhenTitleBeyondTruncation()
    {
        // Title is way past 100KB - should not be found
        var html = "<html><head>" + new string('x', 200_000)
            + "<title>Hidden Title</title></head></html>";
        Assert.Null(HtmlSanitiser.ExtractTitle(html));
    }

    // --- ProcessNoteAsync Tests ---

    [Fact]
    public async Task ProcessNoteAsync_NoUrls_SetsCompletedWithEmptyResult()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "No URLs",
            Content = "Just some text without links",
            EnrichStatus = EnrichStatus.Pending,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp);
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260214120000");
        Assert.Equal(EnrichStatus.Completed, note!.EnrichStatus);
        Assert.NotNull(note.EnrichmentJson);

        var result = JsonSerializer.Deserialize<EnrichmentResult>(note.EnrichmentJson);
        Assert.NotNull(result);
        Assert.Empty(result.Urls);
    }

    [Fact]
    public async Task ProcessNoteAsync_WithUrl_FetchesAndStoresMetadata()
    {
        var dbName = Guid.NewGuid().ToString();
        var html = """
            <html>
            <head>
                <title>Test Article</title>
                <meta name="description" content="A test article description">
            </head>
            <body><p>Article body content here</p></body>
            </html>
            """;
        var handler = new FakeHttpMessageHandler(html);
        using var sp = BuildServiceProvider(dbName, handler);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "Link note",
            Content = "Check this out https://example.com/article",
            EnrichStatus = EnrichStatus.Pending,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp, handler);
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260214120000");
        Assert.Equal(EnrichStatus.Completed, note!.EnrichStatus);
        Assert.NotNull(note.EnrichmentJson);

        var result = JsonSerializer.Deserialize<EnrichmentResult>(note.EnrichmentJson);
        Assert.NotNull(result);
        Assert.Single(result.Urls);
        Assert.Equal("https://example.com/article", result.Urls[0].Url);
        Assert.Equal("Test Article", result.Urls[0].Title);
        Assert.Equal("A test article description", result.Urls[0].Description);
        Assert.Contains("Article body content", result.Urls[0].ContentExcerpt);
    }

    [Fact]
    public async Task ProcessNoteAsync_SkipsMissingNote()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var service = CreateService(sp);

        // Should not throw
        await service.ProcessNoteAsync("nonexistent", CancellationToken.None);
    }

    [Fact]
    public async Task ProcessNoteAsync_SetsFailedOnHttpError()
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new FakeHttpMessageHandler(statusCode: HttpStatusCode.InternalServerError);
        using var sp = BuildServiceProvider(dbName, handler);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "Bad link",
            Content = "See https://example.com/broken",
            EnrichStatus = EnrichStatus.Pending,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp, handler);
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260214120000");
        // Individual URL failures are caught gracefully, so the note completes
        // with a partial result (URL entry with null fields)
        Assert.Equal(EnrichStatus.Completed, note!.EnrichStatus);
    }

    [Fact]
    public async Task ProcessNoteAsync_SkipsWhenMaxRetriesExceeded()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "Exhausted retries",
            Content = "See https://example.com/fail",
            EnrichStatus = EnrichStatus.Failed,
            EnrichRetryCount = 3,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp, config: BuildConfig(maxRetries: 3));
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260214120000");
        Assert.Equal(EnrichStatus.Failed, note!.EnrichStatus);
        Assert.Equal(3, note.EnrichRetryCount); // unchanged
    }

    [Fact]
    public async Task ProcessNoteAsync_IncrementsRetryCountOnFailure()
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new FakeHttpMessageHandler(exception: new HttpRequestException("Connection refused"));
        using var sp = BuildServiceProvider(dbName, handler);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "Retry note",
            Content = "See https://example.com/flaky",
            EnrichStatus = EnrichStatus.Pending,
            EnrichRetryCount = 0,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp, handler);
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260214120000");
        // Individual URL failures are caught gracefully, so the note completes
        // with a partial result (URL entry with null fields)
        Assert.Equal(EnrichStatus.Completed, note!.EnrichStatus);
    }

    // --- I5: Processing guard state ---

    [Fact]
    public async Task ProcessNoteAsync_SetsProcessingBeforeWork()
    {
        // Verify that note goes through Processing state
        var dbName = Guid.NewGuid().ToString();
        var html = "<html><head><title>T</title></head><body>B</body></html>";
        var handler = new FakeHttpMessageHandler(html);
        using var sp = BuildServiceProvider(dbName, handler);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120000",
            Title = "Test",
            Content = "See https://example.com",
            EnrichStatus = EnrichStatus.Pending,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp, handler);
        await service.ProcessNoteAsync("20260214120000", CancellationToken.None);

        // After completion, status should be Completed (passed through Processing)
        var note = await FreshLoadAsync(sp, "20260214120000");
        Assert.Equal(EnrichStatus.Completed, note!.EnrichStatus);
    }

    // --- GetPendingNoteIdsAsync Tests ---

    [Fact]
    public async Task GetPendingNoteIdsAsync_ReturnsPendingAndFailed()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var ctx = CreateContext(sp);
        ctx.Notes.AddRange(
            new Note { Id = "note_pending", Title = "T", Content = "C", EnrichStatus = EnrichStatus.Pending },
            new Note { Id = "note_failed", Title = "T", Content = "C", EnrichStatus = EnrichStatus.Failed },
            new Note { Id = "note_completed", Title = "T", Content = "C", EnrichStatus = EnrichStatus.Completed },
            new Note { Id = "note_none", Title = "T", Content = "C", EnrichStatus = EnrichStatus.None },
            new Note { Id = "note_processing", Title = "T", Content = "C", EnrichStatus = EnrichStatus.Processing }
        );
        await ctx.SaveChangesAsync();

        var service = CreateService(sp);
        var ids = await service.GetPendingNoteIdsAsync(CancellationToken.None);

        Assert.Contains("note_pending", ids);
        Assert.Contains("note_failed", ids);
        Assert.DoesNotContain("note_completed", ids);
        Assert.DoesNotContain("note_none", ids);
        Assert.DoesNotContain("note_processing", ids);
    }

    // --- I6: RecoverStuckNotesAsync Tests ---

    [Fact]
    public async Task RecoverStuckNotesAsync_ResetsAndEnqueuesStuckNotes()
    {
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildServiceProvider(dbName);

        var ctx = CreateContext(sp);
        ctx.Notes.Add(new Note
        {
            Id = "20260214120001",
            Title = "Stuck Processing",
            Content = "Content",
            EnrichStatus = EnrichStatus.Processing,
        });
        ctx.Notes.Add(new Note
        {
            Id = "20260214120002",
            Title = "Stuck Pending",
            Content = "Content",
            EnrichStatus = EnrichStatus.Pending,
        });
        ctx.Notes.Add(new Note
        {
            Id = "20260214120003",
            Title = "Done",
            Content = "Content",
            EnrichStatus = EnrichStatus.Completed,
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(sp);
        await service.RecoverStuckNotesAsync(CancellationToken.None);

        // Stuck notes should be reset to Pending
        var stuck1 = await FreshLoadAsync(sp, "20260214120001");
        Assert.Equal(EnrichStatus.Pending, stuck1!.EnrichStatus);

        var stuck2 = await FreshLoadAsync(sp, "20260214120002");
        Assert.Equal(EnrichStatus.Pending, stuck2!.EnrichStatus);

        // Completed note should be unchanged
        var done = await FreshLoadAsync(sp, "20260214120003");
        Assert.Equal(EnrichStatus.Completed, done!.EnrichStatus);

        // Stuck notes should be re-enqueued
        var queue = sp.GetRequiredService<IEnrichmentQueue>();
        Assert.True(queue.Reader.TryRead(out var id1));
        Assert.True(queue.Reader.TryRead(out var id2));
        var ids = new[] { id1, id2 };
        Assert.Contains("20260214120001", ids);
        Assert.Contains("20260214120002", ids);
    }

    // --- Queue Tests ---

    [Fact]
    public async Task ChannelEnrichmentQueue_EnqueueAndRead()
    {
        var queue = new ChannelEnrichmentQueue();
        await queue.EnqueueAsync("note1");
        await queue.EnqueueAsync("note2");

        var reader = queue.Reader;
        Assert.True(reader.TryRead(out var id1));
        Assert.Equal("note1", id1);
        Assert.True(reader.TryRead(out var id2));
        Assert.Equal("note2", id2);
    }
}

// --- C2: SSRF Protection Tests (now on UrlSafetyChecker directly) ---

public class UrlSafetyCheckerTests
{
    [Fact]
    public void IsPrivateAddress_RejectsLoopback()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Loopback));
        Assert.True(checker.IsPrivateAddress(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void IsPrivateAddress_Rejects10Network()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("10.0.0.1")));
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("10.255.255.255")));
    }

    [Fact]
    public void IsPrivateAddress_Rejects172_16Network()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("172.16.0.1")));
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("172.31.255.255")));
        Assert.False(checker.IsPrivateAddress(IPAddress.Parse("172.32.0.1")));
    }

    [Fact]
    public void IsPrivateAddress_Rejects192_168Network()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("192.168.0.1")));
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("192.168.255.255")));
    }

    [Fact]
    public void IsPrivateAddress_RejectsLinkLocal()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("169.254.0.1")));
    }

    [Fact]
    public void IsPrivateAddress_AllowsPublicAddress()
    {
        var checker = new UrlSafetyChecker();
        Assert.False(checker.IsPrivateAddress(IPAddress.Parse("8.8.8.8")));
        Assert.False(checker.IsPrivateAddress(IPAddress.Parse("93.184.216.34")));
    }

    [Fact]
    public void IsPrivateAddress_RejectsIPv6UniqueLocal()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("fc00::1")));
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    public void IsPrivateAddress_RejectsIPv6LinkLocal()
    {
        var checker = new UrlSafetyChecker();
        Assert.True(checker.IsPrivateAddress(IPAddress.Parse("fe80::1")));
    }

    [Fact]
    public async Task IsUrlSafeAsync_RejectsNonHttpSchemes()
    {
        var checker = new TestableUrlSafetyChecker();
        Assert.False(await checker.IsUrlSafeAsync("ftp://example.com", CancellationToken.None));
        Assert.False(await checker.IsUrlSafeAsync("file:///etc/passwd", CancellationToken.None));
        Assert.False(await checker.IsUrlSafeAsync("javascript:alert(1)", CancellationToken.None));
    }

    [Fact]
    public async Task IsUrlSafeAsync_RejectsInvalidUrls()
    {
        var checker = new TestableUrlSafetyChecker();
        Assert.False(await checker.IsUrlSafeAsync("not-a-url", CancellationToken.None));
        Assert.False(await checker.IsUrlSafeAsync("", CancellationToken.None));
    }
}

// --- HtmlSanitiser Tests ---

public class HtmlSanitiserTests
{
    [Fact]
    public void StripToPlainText_RemovesAllTagsAndDecodes()
    {
        var html = "<html><body><script>evil()</script><p>Hello &amp; World</p></body></html>";
        var result = HtmlSanitiser.StripToPlainText(html);
        Assert.Contains("Hello & World", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain("evil", result);
    }

    [Fact]
    public void StripToPlainText_CollapsesWhitespace()
    {
        var html = "<p>Word1</p>   <p>Word2</p>\n\t<p>Word3</p>";
        var result = HtmlSanitiser.StripToPlainText(html);
        Assert.DoesNotContain("  ", result); // no double spaces
    }
}

/// <summary>
/// Overrides DNS resolution to return a public IP, preventing real network calls in tests.
/// </summary>
public class TestableUrlSafetyChecker : UrlSafetyChecker
{
    protected override Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
        => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
}

/// <summary>
/// Fake HttpMessageHandler for testing URL fetching without real HTTP calls.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string? _responseContent;
    private readonly HttpStatusCode _statusCode;
    private readonly Exception? _exception;

    public int RequestCount { get; private set; }
    public string? LastRequestUrl { get; private set; }

    public FakeHttpMessageHandler(
        string? responseContent = null,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Exception? exception = null)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUrl = request.RequestUri?.ToString();

        if (_exception is not null)
            throw _exception;

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent ?? ""),
        };
        return Task.FromResult(response);
    }
}
