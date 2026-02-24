using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;

namespace ZettelWeb.Services.Publishing;

/// <summary>Publishes blog posts to a GitHub repository as Astro draft files.</summary>
public partial class GitHubPublishingService : IPublishingService
{
    private readonly GitHubOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<GitHubPublishingService> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public GitHubPublishingService(
        IOptions<PublishingOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<GitHubPublishingService> logger)
    {
        _options = options.Value.GitHub;
        _http = httpFactory.CreateClient("GitHub");
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string> SendToDraftAsync(ContentPiece piece, CancellationToken ct = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("publishing.github.send_to_draft");
        activity?.SetTag("content.piece_id", piece.Id);
        activity?.SetTag("content.medium", piece.Medium);

        var sw = Stopwatch.StartNew();
        try
        {
            var slug = BuildSlug(piece.Title ?? "untitled") + "-" + piece.Id[..8];
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var filePath = $"{_options.ContentPath}/{date}-{slug}.md";
            var fileContent = BuildFileContent(piece);
            var contentBytes = Encoding.UTF8.GetBytes(fileContent);
            var contentBase64 = Convert.ToBase64String(contentBytes);

            // Check if file already exists so we can supply its SHA (required for updates).
            var sha = await GetExistingShaAsync(filePath, ct);

            var payload = new Dictionary<string, object?>
            {
                ["message"] = $"draft: {piece.Title ?? slug}",
                ["content"] = contentBase64,
                ["branch"] = _options.Branch,
            };
            if (sha is not null)
                payload["sha"] = sha;

            var apiUrl = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/contents/{filePath}";
            var request = new HttpRequestMessage(HttpMethod.Put, apiUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOpts),
                    Encoding.UTF8,
                    "application/json"),
            };
            ApplyHeaders(request);

            var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException(
                        "GitHub API returned 401 Unauthorized. Check that PublishingOptions:GitHub:Token is valid and not expired.");

                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
                response.EnsureSuccessStatusCode(); // rethrow so caller sees the exception
            }

            _logger.LogInformation(
                "Blog post pushed to GitHub: {Owner}/{Repo}/{Path}",
                _options.Owner, _options.Repo, filePath);

            sw.Stop();
            ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            ZettelTelemetry.DraftsSent.Add(1);

            return $"https://github.com/{_options.Owner}/{_options.Repo}/blob/{_options.Branch}/{filePath}";
        }
        catch
        {
            sw.Stop();
            ZettelTelemetry.PublishingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            ZettelTelemetry.DraftSendFailures.Add(1);
            throw;
        }
    }

    private async Task<string?> GetExistingShaAsync(string filePath, CancellationToken ct)
    {
        var apiUrl = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/contents/{filePath}";
        var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        ApplyHeaders(request);

        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    "GitHub API returned 401 Unauthorized. Check that PublishingOptions:GitHub:Token is valid and not expired.");

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode(); // rethrow so caller sees the exception
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
    }

    private string BuildFileContent(ContentPiece piece)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"author: {_options.Author}");
        sb.AppendLine($"title: {piece.Title ?? "Untitled"}");
        sb.AppendLine($"pubDatetime: {DateTime.UtcNow:O}");
        sb.AppendLine($"description: {piece.Description ?? ""}");
        sb.AppendLine("draft: true");
        var tagsJson = string.Join(", ", (piece.GeneratedTags ?? []).Select(t => $"\"{t}\""));
        sb.AppendLine($"tags: [{tagsJson}]");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(piece.Body);
        return sb.ToString();
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.Add("User-Agent", "ZettelWeb/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    private static string BuildSlug(string title)
    {
        var slug = InvalidSlugCharsRegex().Replace(title.ToLowerInvariant(), "-");
        slug = MultiHyphenRegex().Replace(slug, "-").Trim('-');
        return slug.Length > 80 ? slug[..80] : slug;
    }

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex InvalidSlugCharsRegex();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex MultiHyphenRegex();
}
