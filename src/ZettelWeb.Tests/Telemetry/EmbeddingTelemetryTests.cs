using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Telemetry;

public class EmbeddingTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _activities = [];

    public EmbeddingTelemetryTests()
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

    private (EmbeddingBackgroundService service, ZettelDbContext db) CreateService()
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
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f }));
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:MaxInputCharacters"] = "4000",
                ["Embedding:MaxRetries"] = "3",
            })
            .Build();

        var queue = new FakeEmbeddingQueue();

        return (new EmbeddingBackgroundService(
            queue, sp,
            NullLogger<EmbeddingBackgroundService>.Instance,
            config), db);
    }

    [Fact]
    public async Task ProcessNoteAsync_EmitsActivityOnSuccess()
    {
        var (service, db) = CreateService();
        var noteId = $"test-embed-{Guid.NewGuid():N}";
        var note = new Note
        {
            Id = noteId,
            Title = "Test",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Pending,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        await service.ProcessNoteAsync(noteId, CancellationToken.None);

        var activity = Assert.Single(_activities,
            a => a.OperationName == "embedding.process"
                 && a.GetTagItem("note.id")?.ToString() == noteId);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task ProcessNoteAsync_EmitsActivityOnNotFound()
    {
        var (service, _) = CreateService();
        var noteId = $"nonexistent-{Guid.NewGuid():N}";

        await service.ProcessNoteAsync(noteId, CancellationToken.None);

        var activity = Assert.Single(_activities,
            a => a.OperationName == "embedding.process"
                 && a.GetTagItem("note.id")?.ToString() == noteId);
    }
}
