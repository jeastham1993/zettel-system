using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

public partial class EnrichmentBackgroundService : BackgroundService
{
    private readonly IEnrichmentQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnrichmentBackgroundService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUrlSafetyChecker _urlSafetyChecker;
    private readonly int _timeoutSeconds;
    private readonly int _maxRetries;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private const int MaxHtmlBytes = 512_000; // 512KB max response body

    public EnrichmentBackgroundService(
        IEnrichmentQueue queue,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IUrlSafetyChecker urlSafetyChecker,
        ILogger<EnrichmentBackgroundService> logger,
        IConfiguration configuration)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _urlSafetyChecker = urlSafetyChecker;
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
                    if (!await _urlSafetyChecker.IsUrlSafeAsync(url, cancellationToken))
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
            Title = HtmlSanitiser.ExtractTitle(html),
            Description = HtmlSanitiser.ExtractDescription(html),
            ContentExcerpt = HtmlSanitiser.ExtractContentExcerpt(html),
            FetchedAt = DateTime.UtcNow,
        };
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
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
