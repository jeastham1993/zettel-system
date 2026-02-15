using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public partial class CaptureService
{
    private readonly INoteService _noteService;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly ZettelDbContext _db;
    private readonly CaptureConfig _config;
    private readonly ILogger<CaptureService> _logger;

    public CaptureService(
        INoteService noteService,
        IEnrichmentQueue enrichmentQueue,
        ZettelDbContext db,
        IOptions<CaptureConfig> config,
        ILogger<CaptureService> logger)
    {
        _noteService = noteService;
        _enrichmentQueue = enrichmentQueue;
        _db = db;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<Note?> CaptureAsync(string content, string source, IEnumerable<string>? tags = null)
    {
        var allTags = new List<string> { $"source:{source}" };
        if (tags is not null)
            allTags.AddRange(tags);

        var note = await _noteService.CreateFleetingAsync(content, source, allTags);

        if (UrlRegex().IsMatch(content))
        {
            // I7: Set EnrichStatus=Pending before enqueuing (outbox pattern)
            var tracked = await _db.Notes.FindAsync(note.Id);
            if (tracked is not null)
            {
                tracked.EnrichStatus = EnrichStatus.Pending;
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation("URLs detected in note {NoteId}, enqueuing for enrichment", note.Id);
            await _enrichmentQueue.EnqueueAsync(note.Id);
        }

        return note;
    }

    public (string content, bool isValid) ParseEmailPayload(JsonElement payload)
    {
        string? from = null;
        string? subject = null;
        string? body = null;

        if (payload.TryGetProperty("from", out var fromEl))
            from = fromEl.GetString();

        if (from is null || !IsAllowedEmailSender(from))
            return (string.Empty, false);

        if (payload.TryGetProperty("subject", out var subjectEl))
            subject = subjectEl.GetString();

        if (payload.TryGetProperty("text", out var textEl))
            body = textEl.GetString();

        if (string.IsNullOrWhiteSpace(body) && payload.TryGetProperty("html", out var htmlEl))
            body = StripHtml(htmlEl.GetString());

        if (string.IsNullOrWhiteSpace(body))
            return (string.Empty, false);

        var content = string.IsNullOrWhiteSpace(subject)
            ? body!.Trim()
            : $"{subject}\n\n{body!.Trim()}";

        return (content, true);
    }

    public (string content, bool isValid) ParseSesNotification(JsonElement notification)
    {
        if (!notification.TryGetProperty("mail", out var mail))
            return (string.Empty, false);

        // Extract sender from mail.source (most reliable)
        string? sender = null;
        if (mail.TryGetProperty("source", out var sourceEl))
            sender = sourceEl.GetString();

        if (sender is null || !IsAllowedEmailSender(sender))
            return (string.Empty, false);

        // Extract subject from commonHeaders
        string? subject = null;
        if (mail.TryGetProperty("commonHeaders", out var headers) &&
            headers.TryGetProperty("subject", out var subjectEl))
            subject = subjectEl.GetString();

        // Extract body from raw MIME content
        if (!notification.TryGetProperty("content", out var contentEl))
            return (string.Empty, false);

        var mimeContent = contentEl.GetString();
        if (string.IsNullOrWhiteSpace(mimeContent))
            return (string.Empty, false);

        var body = ExtractMimeTextBody(mimeContent);
        if (string.IsNullOrWhiteSpace(body))
            return (string.Empty, false);

        var result = string.IsNullOrWhiteSpace(subject)
            ? body.Trim()
            : $"{subject}\n\n{body.Trim()}";

        return (result, true);
    }

    private static string? ExtractMimeTextBody(string mime)
    {
        // Check if multipart by looking for boundary in Content-Type header
        var boundaryMatch = MimeBoundaryRegex().Match(mime);
        if (boundaryMatch.Success)
        {
            var boundary = boundaryMatch.Groups[1].Value;
            return ExtractPlainTextPart(mime, boundary);
        }

        // Simple single-part: body is after the first blank line
        var headerEnd = mime.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            headerEnd = mime.IndexOf("\n\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            return null;

        var separatorLen = mime[headerEnd] == '\r' ? 4 : 2;
        return mime[(headerEnd + separatorLen)..].TrimEnd();
    }

    private static string? ExtractPlainTextPart(string mime, string boundary)
    {
        var parts = mime.Split("--" + boundary);
        foreach (var part in parts)
        {
            if (part.StartsWith("--")) continue; // closing boundary

            // Check if this part is text/plain
            if (part.Contains("Content-Type: text/plain", StringComparison.OrdinalIgnoreCase))
            {
                // Body is after blank line within this part
                var bodyStart = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (bodyStart < 0)
                    bodyStart = part.IndexOf("\n\n", StringComparison.Ordinal);
                if (bodyStart < 0) continue;

                var separatorLen = part[bodyStart] == '\r' ? 4 : 2;
                return part[(bodyStart + separatorLen)..].TrimEnd();
            }
        }

        return null;
    }

    public (string content, bool isValid) ParseTelegramUpdate(JsonElement update)
    {
        if (!update.TryGetProperty("message", out var message))
            return (string.Empty, false);

        if (!message.TryGetProperty("chat", out var chat) ||
            !chat.TryGetProperty("id", out var chatIdEl))
            return (string.Empty, false);

        var chatId = chatIdEl.GetInt64();
        if (!IsAllowedTelegramChat(chatId))
            return (string.Empty, false);

        string? text = null;
        if (message.TryGetProperty("text", out var textEl))
            text = textEl.GetString();

        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, false);

        return (text.Trim(), true);
    }

    public bool IsAllowedEmailSender(string fromAddress)
    {
        // Handle "Name <email>" format
        var email = ExtractEmail(fromAddress);
        return _config.AllowedEmailSenders
            .Any(s => string.Equals(s, email, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsAllowedTelegramChat(long chatId)
    {
        return _config.AllowedTelegramChatIds.Contains(chatId);
    }

    private static string ExtractEmail(string from)
    {
        var match = EmailExtractRegex().Match(from);
        return match.Success ? match.Groups[1].Value : from.Trim();
    }

    private static string? StripHtml(string? html)
    {
        if (html is null) return null;
        return HtmlTagRegex().Replace(html, "").Trim();
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"<([^>]+)>")]
    private static partial Regex EmailExtractRegex();

    [GeneratedRegex(@"boundary=""?([^""\s;]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex MimeBoundaryRegex();
}
