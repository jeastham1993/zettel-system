using ZettelWeb.Background;

namespace ZettelWeb.Tests.Background;

public class EmbeddingQueueTests
{
    [Fact]
    public async Task EnqueueAndDequeue_ReturnsNoteId()
    {
        var queue = new ChannelEmbeddingQueue();

        await queue.EnqueueAsync("20260213120000");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var noteId = await queue.Reader.ReadAsync(cts.Token);

        Assert.Equal("20260213120000", noteId);
    }

    [Fact]
    public async Task Enqueue_MultipleItems_AllDequeued()
    {
        var queue = new ChannelEmbeddingQueue();

        await queue.EnqueueAsync("note1");
        await queue.EnqueueAsync("note2");
        await queue.EnqueueAsync("note3");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
            ids.Add(await queue.Reader.ReadAsync(cts.Token));

        Assert.Equal(new[] { "note1", "note2", "note3" }, ids);
    }

    [Fact]
    public async Task Reader_WaitsForItems()
    {
        var queue = new ChannelEmbeddingQueue();

        var readTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return await queue.Reader.ReadAsync(cts.Token);
        });

        await Task.Delay(100);
        await queue.EnqueueAsync("delayed-note");

        var result = await readTask;
        Assert.Equal("delayed-note", result);
    }
}
