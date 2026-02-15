using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb.Background;

namespace ZettelWeb.Health;

public class SqsPollingHealthCheck : IHealthCheck
{
    private readonly SqsPollingBackgroundService _service;

    public SqsPollingHealthCheck(SqsPollingBackgroundService service)
    {
        _service = service;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var lastPoll = _service.LastPollUtc;

        if (lastPoll is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "SQS polling has not completed its first poll yet"));
        }

        var elapsed = DateTime.UtcNow - lastPoll.Value;

        if (elapsed > TimeSpan.FromMinutes(5))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"SQS polling last succeeded {elapsed.TotalMinutes:F1} minutes ago"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"SQS polling last succeeded {elapsed.TotalSeconds:F0} seconds ago"));
    }
}
