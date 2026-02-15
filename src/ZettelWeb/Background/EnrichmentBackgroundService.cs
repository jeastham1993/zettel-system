using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Background;

public partial class EnrichmentBackgroundService : BackgroundService
{
    private readonly IEnrichmentQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnrichmentBackgroundService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _timeoutSeconds;
    private readonly int _maxRetries;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private const int MaxHtmlBytes = 512_000; // 512KB max response body
    private const int HtmlTruncateChars = 102_400; // 100KB regex processing limit

    public EnrichmentBackgroundService(
        IEnrichmentQueue queue,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<EnrichmentBackgroundService> logger,
        IConfiguration configuration)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeoutSeconds = configuration.GetValue("Capture:EnrichmentTimeoutSeconds", 10);
        _maxRetries = configuration.GetValue("Capture:EnrichmentMaxRetries", 3);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStuckNotesAsync(stoppingToken);

        var channelTask = ProcessChannelAsync(stoppingToken);
        var pollTask = PollDatabaseAsync(stoppingToken);

        await Task.WhenAll(channelTask, pollTask);
    }

    private async Task ProcessChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (var noteId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessNoteAsync(noteId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error enriching note {NoteId}", noteId);
            }
        }
    }

    private async Task PollDatabaseAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);

                var noteIds = await GetPendingNoteIdsAsync(stoppingToken);

                foreach (var noteId in noteIds)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessNoteAsync(noteId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DB polling for enrichment");
            }
        }
    }

    public async Task RecoverStuckNotesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var stuckNotes = await db.Notes
            .Where(n => n.EnrichStatus == EnrichStatus.Processing
                     || n.EnrichStatus == EnrichStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var note in stuckNotes)
        {
            note.EnrichStatus = EnrichStatus.Pending;
            _logger.LogWarning(
                "Reset note {NoteId} EnrichStatus to Pending on startup (was {Status})",
                note.Id, note.EnrichStatus);
            await _queue.EnqueueAsync(note.Id, cancellationToken);
        }

        if (stuckNotes.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingNoteIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        return await db.Notes
            .Where(n => n.EnrichStatus == EnrichStatus.Pending
                     || n.EnrichStatus == EnrichStatus.Failed)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task ProcessNoteAsync(string noteId, CancellationToken cancellationToken)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("enrichment.process");
        activity?.SetTag("note.id", noteId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var note = await db.Notes.FindAsync(new object[] { noteId }, cancellationToken);
        if (note is null)
        {
            _logger.LogWarning("Note {NoteId} not found for enrichment", noteId);
            return;
        }

        if (note.EnrichStatus == EnrichStatus.Failed && note.EnrichRetryCount >= _maxRetries)
        {
            _logger.LogWarning("Note {NoteId} exceeded max retries ({MaxRetries}), skipping",
                noteId, _maxRetries);
            return;
        }

        var urls = ExtractUrls(note.Content);
        if (urls.Count == 0)
        {
            note.EnrichStatus = EnrichStatus.Completed;
            note.EnrichmentJson = JsonSerializer.Serialize(new EnrichmentResult { Urls = [] });
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Note {NoteId} has no URLs, marked as completed", noteId);
            activity?.SetTag("enrichment.url_count", 0);
            activity?.SetStatus(ActivityStatusCode.Ok);
            ZettelTelemetry.EnrichmentsProcessed.Add(1);
            return;
        }

        // Set Processing guard state before starting work
        note.EnrichStatus = EnrichStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var urlResults = new List<UrlEnrichment>();

            foreach (var url in urls)
            {
                try
                {
                    if (!await IsUrlSafeAsync(url, cancellationToken))
                    {
                        _logger.LogWarning("Skipping unsafe URL {Url} for note {NoteId} (SSRF protection)",
                            url, noteId);
                        urlResults.Add(new UrlEnrichment
                        {
                            Url = url,
                            Title = null,
                            Description = null,
                            ContentExcerpt = null,
                            FetchedAt = DateTime.UtcNow,
                        });
                        continue;
                    }

                    var enrichment = await FetchUrlMetadataAsync(url, cancellationToken);
                    urlResults.Add(enrichment);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch URL {Url} for note {NoteId}",
                        url, noteId);
                    urlResults.Add(new UrlEnrichment
                    {
                        Url = url,
                        Title = null,
                        Description = null,
                        ContentExcerpt = null,
                        FetchedAt = DateTime.UtcNow,
                    });
                }
            }

            var result = new EnrichmentResult { Urls = urlResults };
            note.EnrichmentJson = JsonSerializer.Serialize(result);
            note.EnrichStatus = EnrichStatus.Completed;
            note.EnrichRetryCount = 0;

            _logger.LogInformation("Enriched note {NoteId} with {UrlCount} URLs",
                noteId, urlResults.Count);

            activity?.SetTag("enrichment.url_count", urlResults.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            ZettelTelemetry.EnrichmentsProcessed.Add(1);
        }
        catch (Exception ex)
        {
            note.EnrichStatus = EnrichStatus.Failed;
            note.EnrichRetryCount++;

            _logger.LogError(ex, "Failed to enrich note {NoteId}", noteId);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            ZettelTelemetry.EnrichmentsFailed.Add(1);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<string> ExtractUrls(string content)
    {
        var matches = UrlRegex().Matches(content);
        return matches.Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']')).Distinct().ToList();
    }

    public async Task<bool> IsUrlSafeAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;

        try
        {
            var addresses = await ResolveHostAsync(uri.Host, cancellationToken);
            foreach (var addr in addresses)
            {
                if (IsPrivateAddress(addr))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    protected virtual async Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }

    public static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 caught by IsLoopback. fc00::/7 (unique local) and fe80::/10 (link-local)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true; // fc00::/7
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true; // fe80::/10
            return false;
        }

        var ipBytes = address.GetAddressBytes();
        // 10.0.0.0/8
        if (ipBytes[0] == 10) return true;
        // 172.16.0.0/12
        if (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31) return true;
        // 192.168.0.0/16
        if (ipBytes[0] == 192 && ipBytes[1] == 168) return true;
        // 127.0.0.0/8
        if (ipBytes[0] == 127) return true;
        // 169.254.0.0/16
        if (ipBytes[0] == 169 && ipBytes[1] == 254) return true;

        return false;
    }

    public async Task<UrlEnrichment> FetchUrlMetadataAsync(string url, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        var client = _httpClientFactory.CreateClient("Enrichment");
        var response = await client.GetAsync(url, cts.Token);
        response.EnsureSuccessStatusCode();

        // C4: Bounded response body - read max 512KB to prevent OOM
        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxHtmlBytes];
        var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        var html = new string(buffer, 0, charsRead);

        return new UrlEnrichment
        {
            Url = url,
            Title = ExtractTitle(html),
            Description = ExtractDescription(html),
            ContentExcerpt = ExtractContentExcerpt(html),
            FetchedAt = DateTime.UtcNow,
        };
    }

    public static string? ExtractTitle(string html)
    {
        // Truncate before regex to prevent ReDoS
        var truncated = html.Length > HtmlTruncateChars ? html[..HtmlTruncateChars] : html;
        var match = TitleRegex().Match(truncated);
        if (!match.Success) return null;

        var title = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(title) ? null : System.Net.WebUtility.HtmlDecode(title);
    }

    public static string? ExtractDescription(string html)
    {
        var truncated = html.Length > HtmlTruncateChars ? html[..HtmlTruncateChars] : html;
        var match = MetaDescriptionRegex().Match(truncated);
        if (!match.Success)
            match = MetaDescriptionAltRegex().Match(truncated);
        if (!match.Success) return null;

        var desc = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(desc) ? null : System.Net.WebUtility.HtmlDecode(desc);
    }

    public static string? ExtractContentExcerpt(string html)
    {
        var truncated = html.Length > HtmlTruncateChars ? html[..HtmlTruncateChars] : html;
        var bodyMatch = BodyRegex().Match(truncated);
        var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : truncated;

        body = ScriptStyleRegex().Replace(body, " ");
        body = HtmlTagRegex().Replace(body, " ");
        body = System.Net.WebUtility.HtmlDecode(body);
        body = WhitespaceRegex().Replace(body, " ").Trim();

        if (string.IsNullOrWhiteSpace(body)) return null;

        return body.Length > 500 ? body[..500] : body;
    }

    // C3: All regexes converted to [GeneratedRegex] with NonBacktracking where applicable

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<meta\s+(?:[^>]*?\s+)?(?:name\s*=\s*[""']description[""']|property\s*=\s*[""']og:description[""'])\s+(?:[^>]*?\s+)?content\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex(@"<meta\s+(?:[^>]*?\s+)?content\s*=\s*[""']([^""']*)[""']\s+(?:[^>]*?\s+)?(?:name\s*=\s*[""']description[""']|property\s*=\s*[""']og:description[""'])", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescriptionAltRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.NonBacktracking)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>|<nav[^>]*>.*?</nav>|<header[^>]*>.*?</header>|<footer[^>]*>.*?</footer>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex ScriptStyleRegex();
}

public record EnrichmentResult
{
    public List<UrlEnrichment> Urls { get; init; } = [];
}

public record UrlEnrichment
{
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ContentExcerpt { get; init; }
    public DateTime FetchedAt { get; init; }
}
