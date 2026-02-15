using System.Threading.Channels;

namespace ZettelWeb.Background;

public interface IEnrichmentQueue
{
    Task EnqueueAsync(string noteId, CancellationToken cancellationToken = default);
    ChannelReader<string> Reader { get; }
}

public class ChannelEnrichmentQueue : IEnrichmentQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _channel.Reader;

    public async Task EnqueueAsync(string noteId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(noteId, cancellationToken);
    }
}
