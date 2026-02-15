using System.Threading.Channels;
using ZettelWeb.Background;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Shared fake IEmbeddingQueue that records enqueued IDs for assertions.
/// Replaces the private FakeEmbeddingQueue in ImportServiceTests.
/// </summary>
public class FakeEmbeddingQueue : IEmbeddingQueue
{
    public List<string> EnqueuedIds { get; } = new();

    public ChannelReader<string> Reader =>
        Channel.CreateUnbounded<string>().Reader;

    public Task EnqueueAsync(string noteId,
        CancellationToken cancellationToken = default)
    {
        EnqueuedIds.Add(noteId);
        return Task.CompletedTask;
    }
}
