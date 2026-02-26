using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Services;

namespace ZettelWeb.Background;

/// <summary>
/// Processes research agenda execution jobs from the queue.
/// Owns its own DI scope per job, so the ZettelDbContext lifecycle is
/// independent of the HTTP request that enqueued the job (fixes C1).
///
/// Sequential processing (single reader channel) means at most one
/// research run executes at a time — natural concurrency guard (fixes C3).
/// </summary>
public class ResearchExecutionBackgroundService : BackgroundService
{
    private readonly IResearchExecutionQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ResearchExecutionBackgroundService> _logger;

    public ResearchExecutionBackgroundService(
        IResearchExecutionQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ResearchExecutionBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Research execution background service started");

        // C2: reset any agendas stuck in Executing from a previous run/crash
        using (var scope = _serviceProvider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IResearchAgentService>();
            await svc.RecoverStuckAgendasAsync(stoppingToken);
        }

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Research agenda execution failed for {AgendaId}", job.AgendaId);
            }
        }
    }

    private async Task RunJobAsync(ResearchExecutionJob job, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting research execution for agenda {AgendaId}", job.AgendaId);

        // Create a fresh scope per job — avoids the disposed-DbContext problem (C1)
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResearchAgentService>();

        await service.ExecuteAgendaAsync(
            job.AgendaId,
            job.BlockedTaskIds,
            stoppingToken);

        _logger.LogInformation("Research execution completed for agenda {AgendaId}", job.AgendaId);
    }
}
