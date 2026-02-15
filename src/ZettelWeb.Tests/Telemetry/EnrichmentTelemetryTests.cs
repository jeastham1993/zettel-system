using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Telemetry;

public class EnrichmentTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _activities = [];

    public EnrichmentTelemetryTests()
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

    private (EnrichmentBackgroundService service, ZettelDbContext db) CreateService()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new ZettelDbContext(options);

        var services = new ServiceCollection();
        services.AddScoped(_ => new ZettelDbContext(
            new DbContextOptionsBuilder<ZettelDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options));
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Capture:EnrichmentTimeoutSeconds"] = "10",
                ["Capture:EnrichmentMaxRetries"] = "3",
            })
            .Build();

        var httpFactory = new TestHttpClientFactory();
        var queue = new FakeEnrichmentQueue();

        return (new EnrichmentBackgroundService(
            queue, sp, httpFactory,
            NullLogger<EnrichmentBackgroundService>.Instance,
            config), db);
    }

    [Fact]
    public async Task ProcessNoteAsync_EmitsActivity_WhenNoUrls()
    {
        var (service, db) = CreateService();
        var note = new Note
        {
            Id = "test-enrich-001",
            Title = "Test",
            Content = "No URLs here",
            EnrichStatus = EnrichStatus.Pending,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        _activities.Clear();

        await service.ProcessNoteAsync("test-enrich-001", CancellationToken.None);

        var activity = Assert.Single(_activities, a => a.OperationName == "enrichment.process");
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task ProcessNoteAsync_EmitsActivity_WhenNotFound()
    {
        var (service, _) = CreateService();
        _activities.Clear();

        await service.ProcessNoteAsync("nonexistent", CancellationToken.None);

        var activity = Assert.Single(_activities, a => a.OperationName == "enrichment.process");
        Assert.Equal("nonexistent", activity.GetTagItem("note.id")?.ToString());
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
