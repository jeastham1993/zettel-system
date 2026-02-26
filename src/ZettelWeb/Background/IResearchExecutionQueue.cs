using System.Threading.Channels;

namespace ZettelWeb.Background;

public record ResearchExecutionJob(string AgendaId, IReadOnlyList<string> BlockedTaskIds);

/// <summary>
/// In-memory queue for research agenda execution jobs.
/// Decouples the HTTP request (202 response) from the long-running execution.
/// </summary>
public interface IResearchExecutionQueue
{
    ValueTask EnqueueAsync(ResearchExecutionJob job, CancellationToken cancellationToken = default);
    ChannelReader<ResearchExecutionJob> Reader { get; }
}

public class ChannelResearchExecutionQueue : IResearchExecutionQueue
{
    private readonly Channel<ResearchExecutionJob> _channel =
        Channel.CreateUnbounded<ResearchExecutionJob>(
            new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(ResearchExecutionJob job, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public ChannelReader<ResearchExecutionJob> Reader => _channel.Reader;
}
