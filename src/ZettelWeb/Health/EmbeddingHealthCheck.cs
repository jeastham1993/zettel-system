using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ZettelWeb.Health;

public class EmbeddingHealthCheck : IHealthCheck
{
    private readonly IConfiguration _config;

    public EmbeddingHealthCheck(IConfiguration config)
    {
        _config = config;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = _config["Embedding:Provider"] ?? "openai";

        if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = _config["Embedding:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Embedding service not fully configured"));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy("Embedding service configured"));
    }
}
