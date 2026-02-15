using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ZettelWeb.Data;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Telemetry;

public class NoteServiceTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _activities = [];

    public NoteServiceTelemetryTests()
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

    private static (NoteService service, ZettelDbContext db) CreateService()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ZettelDbContext(options);
        var queue = new FakeEmbeddingQueue();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f });
        return (new NoteService(db, queue, generator), db);
    }

    [Fact]
    public async Task CreateAsync_EmitsActivity()
    {
        var (service, _) = CreateService();

        await service.CreateAsync("Test", "Content");

        Assert.Contains(_activities, a => a.OperationName == "note.create"
            && a.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task GetByIdAsync_EmitsActivity()
    {
        var (service, _) = CreateService();
        var note = await service.CreateAsync("Test", "Content");

        await service.GetByIdAsync(note.Id);

        Assert.Contains(_activities, a => a.OperationName == "note.get"
            && a.GetTagItem("note.id")?.ToString() == note.Id);
    }

    [Fact]
    public async Task DeleteAsync_EmitsActivity()
    {
        var (service, _) = CreateService();
        var note = await service.CreateAsync("Test", "Content");

        await service.DeleteAsync(note.Id);

        Assert.Contains(_activities, a => a.OperationName == "note.delete"
            && a.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task ListAsync_EmitsActivity()
    {
        var (service, _) = CreateService();

        await service.ListAsync();

        Assert.Contains(_activities, a => a.OperationName == "note.list");
    }

    [Fact]
    public async Task UpdateAsync_EmitsActivity()
    {
        var (service, _) = CreateService();
        var note = await service.CreateAsync("Test", "Content");

        await service.UpdateAsync(note.Id, "Updated", "New content");

        Assert.Contains(_activities, a => a.OperationName == "note.update"
            && a.GetTagItem("note.id")?.ToString() == note.Id);
    }
}
