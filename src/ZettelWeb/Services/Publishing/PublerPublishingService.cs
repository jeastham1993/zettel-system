using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;

namespace ZettelWeb.Services.Publishing;

/// <summary>Publishes social content to Publer as draft posts.</summary>
public class PublerPublishingService : IPublishingService
{
    private readonly PublerOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<PublerPublishingService> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public PublerPublishingService(
        IOptions<PublishingOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<PublerPublishingService> logger)
    {
        _options = options.Value.Publer;
        _http = httpFactory.CreateClient("Publer");
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string> SendToDraftAsync(ContentPiece piece, CancellationToken ct = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("publishing.publer.send_to_draft");
        activity?.SetTag("content.piece_id", piece.Id);
        activity?.SetTag("content.medium", piece.Medium);

        var sw = Stopwatch.StartNew();
        try
        {
            // Build account_id list from configured accounts
            var accountIds = _options.Accounts.Select(a => a.Id).ToList();

            var payload = new
            {
                account_ids = accountIds,
                text = piece.Body,
                state = "draft",
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://publer.com/api/v1/posts/schedule")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOpts),
                    Encoding.UTF8,
                    "application/json"),
            };
            ApplyHeaders(request);

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Publer API returned {StatusCode}: {Body}", (int)response.StatusCode, responseBody);
                response.EnsureSuccessStatusCode();
            }

            // Guard against HTML error pages returned with a 2xx status (CDN/WAF redirect,
            // invalid API key causing a login-page redirect, etc.).
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Publer API returned unexpected Content-Type '{ContentType}': {Body}",
                    contentType, responseBody);
                throw new InvalidOperationException(
                    $"Publer API returned non-JSON response (Content-Type: '{contentType}'). " +
                    "Check that the API key is valid and the account ID is correct.");
            }

            using var json = JsonDocument.Parse(responseBody);

            // Publer returns { "post": { ... }, "job_id": "..." } or similar
            var jobId = json.RootElement.TryGetProperty("job_id", out var jobEl)
                ? jobEl.GetString()
                : null;

            if (jobId is not null)
            {
                var postUrl = await PollForPostUrlAsync(jobId, ct);
                _logger.LogInformation("Publer draft created: {JobId} -> {Url}", jobId, postUrl);

                sw.Stop();
                ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
                ZettelTelemetry.DraftsSent.Add(1);

                return postUrl;
            }

            // Some responses embed the post URL directly
            if (json.RootElement.TryGetProperty("post", out var postEl) &&
                postEl.TryGetProperty("share_url", out var urlEl))
            {
                var url = urlEl.GetString() ?? string.Empty;
                _logger.LogInformation("Publer draft created: {Url}", url);

                sw.Stop();
                ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
                ZettelTelemetry.DraftsSent.Add(1);

                return url;
            }

            sw.Stop();
            ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            ZettelTelemetry.DraftsSent.Add(1);

            return "publer:draft:created";
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            sw.Stop();
            ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            ZettelTelemetry.DraftSendFailures.Add(1);
            throw;
        }
    }

    private async Task<string> PollForPostUrlAsync(string jobId, CancellationToken ct)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("publishing.publer.poll_for_url");
        activity?.SetTag("publer.job_id", jobId);

        var attempts = 0;
        for (var i = 0; i < 10; i++)
        {
            attempts++;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://publer.com/api/v1/job_status/{jobId}");
            ApplyHeaders(request);

            var response = await _http.SendAsync(request, ct);
            var pollBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Publer job status returned {StatusCode}: {Body}", (int)response.StatusCode, pollBody);
                continue;
            }

            var pollContentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!pollContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Publer job status returned non-JSON Content-Type '{ContentType}': {Body}", pollContentType, pollBody);
                continue;
            }

            using var json = JsonDocument.Parse(pollBody);

            if (!json.RootElement.TryGetProperty("status", out var statusEl))
                continue;

            var status = statusEl.GetString();
            if (status is "success" or "done")
            {
                activity?.SetTag("publer.poll_attempts", attempts);
                if (json.RootElement.TryGetProperty("post", out var postEl) &&
                    postEl.TryGetProperty("share_url", out var urlEl))
                {
                    return urlEl.GetString()!;
                }
                break;
            }

            if (status is "failed" or "error")
            {
                _logger.LogWarning("Publer job {JobId} failed", jobId);
                break;
            }
        }

        activity?.SetTag("publer.poll_attempts", attempts);
        activity?.SetStatus(ActivityStatusCode.Error, "Publer job did not complete within the polling window");
        throw new InvalidOperationException($"Publer job did not complete within the polling window.");
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        // Publer uses a non-standard scheme: "Bearer-API {key}"
        // TryAddWithoutValidation is required because AuthenticationHeaderValue
        // rejects scheme names containing a hyphen.
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer-API {_options.ApiKey}");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
