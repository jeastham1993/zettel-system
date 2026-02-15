using System.Threading.Channels;
using ZettelWeb.Background;

namespace ZettelWeb.Tests.Fakes;

public class FakeEnrichmentQueue : IEnrichmentQueue
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
