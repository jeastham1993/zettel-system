using System.Threading.Channels;

namespace ZettelWeb.Background;

public interface IEmbeddingQueue
{
    Task EnqueueAsync(string noteId, CancellationToken cancellationToken = default);
    ChannelReader<string> Reader { get; }
}

public class ChannelEmbeddingQueue : IEmbeddingQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _channel.Reader;

    public async Task EnqueueAsync(string noteId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(noteId, cancellationToken);
    }
}
