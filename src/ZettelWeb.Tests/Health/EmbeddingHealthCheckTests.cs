using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb.Health;

namespace ZettelWeb.Tests.Health;

public class EmbeddingHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenOpenAiAndApiKeyConfigured_ReturnsHealthy()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Provider"] = "openai",
                ["Embedding:ApiKey"] = "sk-test-key",
            })
            .Build();

        var check = new EmbeddingHealthCheck(config);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOpenAiAndApiKeyMissing_ReturnsDegraded()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Provider"] = "openai",
                ["Embedding:ApiKey"] = "",
            })
            .Build();

        var check = new EmbeddingHealthCheck(config);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllama_ReturnsHealthyWithoutApiKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Provider"] = "ollama",
            })
            .Build();

        var check = new EmbeddingHealthCheck(config);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_DoesNotExposeProviderOrModelInData()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:Provider"] = "openai",
                ["Embedding:ApiKey"] = "sk-test-key",
            })
            .Build();

        var check = new EmbeddingHealthCheck(config);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Empty(result.Data);
    }
}
