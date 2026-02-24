using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;
using ZettelWeb.Services.Publishing;

namespace ZettelWeb.Tests.Services;

public class PublerPublishingServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ContentPiece MakePiece(string body = "Hello Publer") =>
        new() { Id = "p1", GenerationId = "g1", Medium = "social", Body = body };

    private static (PublerPublishingService svc, StubHttpHandler stub) Build(
        string workspaceId = "ws-123",
        List<PublerAccount>? accounts = null,
        params HttpResponseMessage[] responses)
    {
        accounts ??= [new PublerAccount { Id = "acc-1", Platform = "linkedin" }];
        var stub = new StubHttpHandler(responses);
        var factory = new StubHttpClientFactory(new HttpClient(stub));
        var opts = Options.Create(new PublishingOptions
        {
            Publer = new PublerOptions
            {
                ApiKey = "test-key",
                WorkspaceId = workspaceId,
                Accounts = accounts,
            }
        });
        return (new PublerPublishingService(opts, factory, NullLogger<PublerPublishingService>.Instance), stub);
    }

    // Schedule response: { "success": true, "data": { "job_id": "job-123" } }
    private static HttpResponseMessage ScheduleOk() => Json("""{"success":true,"data":{"job_id":"job-123"}}""");

    // Poll response: complete with no share URL
    private static HttpResponseMessage PollComplete() => Json("""{"success":true,"data":{"status":"complete","result":{"payload":{}}}}""");

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    // ── URL tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendToDraftAsync_ScheduleRequestTargetsAppSubdomain()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        Assert.Equal("app.publer.com", stub.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task SendToDraftAsync_PollRequestTargetsAppSubdomain()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        Assert.Equal("app.publer.com", stub.Requests[1].RequestUri!.Host);
    }

    // ── Header tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendToDraftAsync_IncludesWorkspaceIdHeader()
    {
        var (svc, stub) = Build(workspaceId: "my-workspace", responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        Assert.Equal("my-workspace", stub.Requests[0].Headers.GetValues("Publer-Workspace-Id").Single());
    }

    [Fact]
    public async Task SendToDraftAsync_PollIncludesWorkspaceIdHeader()
    {
        var (svc, stub) = Build(workspaceId: "my-workspace", responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        Assert.Equal("my-workspace", stub.Requests[1].Headers.GetValues("Publer-Workspace-Id").Single());
    }

    // ── Request body tests ────────────────────────────────────────────────────

    [Fact]
    public async Task SendToDraftAsync_SendsBulkWrapper()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        Assert.True(doc.RootElement.TryGetProperty("bulk", out _), "Expected top-level 'bulk' property");
    }

    [Fact]
    public async Task SendToDraftAsync_BulkStateIsDraft()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        Assert.Equal("draft", doc.RootElement.GetProperty("bulk").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendToDraftAsync_BulkPostsContainsAccountsAndNetworks()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece());
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        var post = doc.RootElement.GetProperty("bulk").GetProperty("posts")[0];
        Assert.True(post.TryGetProperty("networks", out _), "Expected 'networks' in post");
        Assert.True(post.TryGetProperty("accounts", out _), "Expected 'accounts' in post");
    }

    [Fact]
    public async Task SendToDraftAsync_NetworksKeyMatchesPlatform()
    {
        var accounts = new List<PublerAccount>
        {
            new() { Id = "acc-li", Platform = "linkedin" },
            new() { Id = "acc-tw", Platform = "twitter" },
        };
        var (svc, stub) = Build(accounts: accounts, responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece("My post"));
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        var networks = doc.RootElement.GetProperty("bulk").GetProperty("posts")[0].GetProperty("networks");
        Assert.True(networks.TryGetProperty("linkedin", out _), "Expected 'linkedin' network");
        Assert.True(networks.TryGetProperty("twitter", out _), "Expected 'twitter' network");
    }

    [Fact]
    public async Task SendToDraftAsync_AccountsListContainsAllAccountIds()
    {
        var accounts = new List<PublerAccount>
        {
            new() { Id = "acc-li", Platform = "linkedin" },
            new() { Id = "acc-tw", Platform = "twitter" },
        };
        var (svc, stub) = Build(accounts: accounts, responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece("My post"));
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        var accountIds = doc.RootElement
            .GetProperty("bulk").GetProperty("posts")[0]
            .GetProperty("accounts")
            .EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToList();
        Assert.Contains("acc-li", accountIds);
        Assert.Contains("acc-tw", accountIds);
    }

    [Fact]
    public async Task SendToDraftAsync_NetworkTextMatchesPieceBody()
    {
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        await svc.SendToDraftAsync(MakePiece("Specific content"));
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        var networks = doc.RootElement.GetProperty("bulk").GetProperty("posts")[0].GetProperty("networks");
        var linkedin = networks.GetProperty("linkedin");
        Assert.Equal("Specific content", linkedin.GetProperty("text").GetString());
    }

    // ── Response parsing tests ─────────────────────────────────────────────────

    [Fact]
    public async Task SendToDraftAsync_ParsesJobIdFromDataProperty()
    {
        // job_id nested under "data", not at root — old code would return fallback string
        var (svc, stub) = Build(responses: [ScheduleOk(), PollComplete()]);
        var result = await svc.SendToDraftAsync(MakePiece());
        // job_id "job-123" must have been read; poll must have been called
        Assert.Equal(2, stub.Requests.Count); // schedule + 1 poll
    }

    [Fact]
    public async Task SendToDraftAsync_RecognisesCompleteStatusFromDataProperty()
    {
        // status "complete" nested under "data" — old code checked root status == "success"
        var pollComplete = Json("""{"success":true,"data":{"status":"complete","result":{}}}""");
        var (svc, stub) = Build(responses: [ScheduleOk(), pollComplete]);
        // Should not throw — loop must exit on "complete"
        var result = await svc.SendToDraftAsync(MakePiece());
        Assert.NotNull(result);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

internal class StubHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _queue = new(responses);

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(ct)
            : string.Empty;
        Bodies.Add(body);
        Requests.Add(request);
        return _queue.TryDequeue(out var r) ? r : new HttpResponseMessage(HttpStatusCode.OK);
    }
}

internal class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
