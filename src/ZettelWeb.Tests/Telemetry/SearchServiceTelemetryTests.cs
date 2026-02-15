using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Telemetry;

public class SearchServiceTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _activities = [];

    public SearchServiceTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ZettelWeb",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private static SearchService CreateService()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f });
        var weights = new SearchWeights();
        return new SearchService(db, generator, weights,
            NullLogger<SearchService>.Instance);
    }

    [Fact]
    public async Task HybridSearchAsync_EmitsActivity()
    {
        var service = CreateService();

        // Will fail on raw SQL but the activity should still be emitted
        try { await service.HybridSearchAsync("test"); } catch { }

        Assert.Contains(_activities, a => a.OperationName == "search.hybrid");
    }

    [Fact]
    public async Task FullTextSearchAsync_EmitsActivity()
    {
        var service = CreateService();

        try { await service.FullTextSearchAsync("test"); } catch { }

        Assert.Contains(_activities, a => a.OperationName == "search.fulltext");
    }

    [Fact]
    public async Task SemanticSearchAsync_EmitsActivity()
    {
        var service = CreateService();

        try { await service.SemanticSearchAsync("test"); } catch { }

        Assert.Contains(_activities, a => a.OperationName == "search.semantic");
    }

    [Fact]
    public async Task EmptyQuery_DoesNotEmitActivity()
    {
        var service = CreateService();

        await service.HybridSearchAsync("");

        Assert.DoesNotContain(_activities, a => a.OperationName == "search.hybrid");
    }
}
