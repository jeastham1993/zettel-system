using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Health;
using ZettelWeb.Tests.Background;

namespace ZettelWeb.Tests.Health;

public class SqsPollingHealthCheckTests
{
    private static SqsPollingBackgroundService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Capture:SqsQueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123/test",
            })
            .Build();

        return new SqsPollingBackgroundService(
            new FakeSqsClient([]),
            new FakeServiceProvider(),
            NullLogger<SqsPollingBackgroundService>.Instance,
            config);
    }

    [Fact]
    public async Task ReportsDegraded_WhenNeverPolled()
    {
        var service = CreateService();
        var healthCheck = new SqsPollingHealthCheck(service);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("first poll", result.Description);
    }

    [Fact]
    public async Task ReportsHealthy_WhenRecentlyPolled()
    {
        var service = CreateService();

        // Start and stop the service to trigger a poll
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        var healthCheck = new SqsPollingHealthCheck(service);
        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReportsUnhealthy_WhenPollIsStale()
    {
        var service = CreateService();

        // Use reflection to set LastPollUtc to a stale time
        var prop = typeof(SqsPollingBackgroundService).GetProperty("LastPollUtc");
        prop!.SetValue(service, DateTime.UtcNow.AddMinutes(-10));

        var healthCheck = new SqsPollingHealthCheck(service);
        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("minutes ago", result.Description);
    }
}

/// <summary>
/// Minimal IServiceProvider for health check tests (SQS service doesn't use scopes in poll).
/// </summary>
internal class FakeServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
