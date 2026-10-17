using System.Net;

namespace ZettelWeb.Tests.Services;

// ── Shared HTTP test doubles ─────────────────────────────────────────────────

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
