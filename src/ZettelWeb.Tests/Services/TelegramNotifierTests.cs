using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Unit tests for TelegramNotifier verifying correct Telegram Bot API calls,
/// error resilience, and broadcast fan-out.
/// </summary>
public class TelegramNotifierTests
{
    private static (TelegramNotifier notifier, StubHttpHandler stub) Build(
        string token = "test-token",
        long[]? chatIds = null,
        params HttpResponseMessage[] responses)
    {
        chatIds ??= [12345L];
        var stub = new StubHttpHandler(responses);
        var factory = new StubHttpClientFactory(new HttpClient(stub));
        var config = Options.Create(new CaptureConfig
        {
            TelegramBotToken = token,
            AllowedTelegramChatIds = chatIds,
        });
        return (new TelegramNotifier(factory, config, NullLogger<TelegramNotifier>.Instance), stub);
    }

    // ── URL ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_PostsToTelegramApiHost()
    {
        var (notifier, stub) = Build();
        await notifier.SendAsync(12345, "Hello");
        Assert.Equal("api.telegram.org", stub.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task SendAsync_PathContainsTokenAndSendMessage()
    {
        var (notifier, stub) = Build(token: "my-token");
        await notifier.SendAsync(12345, "Hello");
        Assert.Contains("/botmy-token/sendMessage", stub.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task SendAsync_UsesHttpPost()
    {
        var (notifier, stub) = Build();
        await notifier.SendAsync(12345, "Hello");
        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_BodyContainsChatId()
    {
        var (notifier, stub) = Build();
        await notifier.SendAsync(99887766, "msg");
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        Assert.Equal(99887766, doc.RootElement.GetProperty("chat_id").GetInt64());
    }

    [Fact]
    public async Task SendAsync_BodyContainsText()
    {
        var (notifier, stub) = Build();
        await notifier.SendAsync(12345, "✅ Note saved.");
        using var doc = JsonDocument.Parse(stub.Bodies[0]);
        Assert.Equal("✅ Note saved.", doc.RootElement.GetProperty("text").GetString());
    }

    // ── Error resilience ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_Non2xxResponse_DoesNotThrow()
    {
        var (notifier, _) = Build(responses: [new HttpResponseMessage(HttpStatusCode.BadRequest)]);
        // Should complete without throwing
        await notifier.SendAsync(12345, "msg");
    }

    [Fact]
    public async Task SendAsync_NetworkException_DoesNotThrow()
    {
        var stub = new ThrowingHttpHandler();
        var factory = new StubHttpClientFactory(new HttpClient(stub));
        var config = Options.Create(new CaptureConfig { TelegramBotToken = "tok", AllowedTelegramChatIds = [1] });
        var notifier = new TelegramNotifier(factory, config, NullLogger<TelegramNotifier>.Instance);
        await notifier.SendAsync(1, "msg"); // should not throw
    }

    // ── BroadcastAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task BroadcastAsync_SendsToEachChatId()
    {
        var (notifier, stub) = Build(chatIds: [111L, 222L, 333L]);
        await notifier.BroadcastAsync("Hello all");
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task BroadcastAsync_EachRequestContainsCorrectChatId()
    {
        var (notifier, stub) = Build(chatIds: [111L, 222L]);
        await notifier.BroadcastAsync("msg");
        var sentIds = stub.Bodies
            .Select(b => JsonDocument.Parse(b).RootElement.GetProperty("chat_id").GetInt64())
            .ToHashSet();
        Assert.Contains(111L, sentIds);
        Assert.Contains(222L, sentIds);
    }

    [Fact]
    public async Task BroadcastAsync_EmptyChatIds_SendsNothing()
    {
        var (notifier, stub) = Build(chatIds: []);
        await notifier.BroadcastAsync("msg");
        Assert.Empty(stub.Requests);
    }
}

// ── Throwing HTTP handler for network-error tests ─────────────────────────────

internal class ThrowingHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => throw new HttpRequestException("Simulated network failure");
}
