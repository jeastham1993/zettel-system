using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenAI;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Lambda;

/// <summary>
/// Builds a minimal DI container for Lambda handler functions.
/// This is a subset of Program.cs — only the services needed by the workers,
/// without HTTP middleware, health checks, or background service hosting.
/// </summary>
internal static class LambdaServiceProvider
{
    private static IServiceProvider? _instance;

    /// <summary>
    /// Returns a singleton service provider, initialised once per Lambda container.
    /// Subsequent calls within the same container reuse the warm instance.
    /// </summary>
    public static IServiceProvider Build()
    {
        return _instance ??= CreateServiceProvider();
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var secretsArn = Environment.GetEnvironmentVariable("APP_SECRETS_ARN");
        var configBuilder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        // In Lambda, load all application config from a single Secrets Manager secret.
        // The secret is a JSON object whose keys use __ as the ASP.NET Core section separator
        // (e.g. "ConnectionStrings__DefaultConnection" → ConnectionStrings:DefaultConnection).
        if (!string.IsNullOrEmpty(secretsArn))
        {
            var secretValues = LoadSecretValues(secretsArn);
            configBuilder.AddInMemoryCollection(secretValues);
        }

        var configuration = configBuilder.Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        // ── Database ────────────────────────────────────────────
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            configuration.GetConnectionString("DefaultConnection"));
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ZettelDbContext>(options =>
            options.UseNpgsql(dataSource, o => o.UseVector()));

        // ── Embedding queue (in-memory, not needed for Lambda workers ─
        // Workers call EmbeddingBackgroundService methods directly.
        services.AddSingleton<IEmbeddingQueue, ChannelEmbeddingQueue>();
        services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();

        // ── Embedding generator (same provider logic as Program.cs) ─
        var embeddingProvider = configuration["Embedding:Provider"] ?? "bedrock";
        var embeddingModel = configuration["Embedding:Model"] ?? "amazon.titan-embed-text-v2:0";

        if (string.Equals(embeddingProvider, "bedrock", StringComparison.OrdinalIgnoreCase))
        {
            var bedrockRegion = configuration["Embedding:BedrockRegion"];
            var client = !string.IsNullOrEmpty(bedrockRegion)
                ? new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(bedrockRegion))
                : new AmazonBedrockRuntimeClient();
            var dimensions = configuration.GetValue<int?>("Embedding:Dimensions");
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                client.AsIEmbeddingGenerator(embeddingModel, dimensions));
        }
        else
        {
            var apiKey = configuration["Embedding:ApiKey"] ?? "";
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                new OpenAIClient(apiKey).GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());
        }

        // ── Services ────────────────────────────────────────────
        services.AddScoped<INoteService>(sp =>
            new NoteService(
                sp.GetRequiredService<ZettelDbContext>(),
                sp.GetRequiredService<IEmbeddingQueue>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<ILogger<NoteService>>()));

        services.AddScoped<CaptureService>();
        services.Configure<CaptureConfig>(configuration.GetSection("Capture"));

        // EmbeddingBackgroundService is registered as a regular (non-hosted) service
        // so Lambda handlers can resolve and call its public methods directly.
        services.AddSingleton<EmbeddingBackgroundService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Fetches the Secrets Manager secret and converts its JSON keys to
    /// ASP.NET Core configuration key format (__ → :).
    /// Synchronous because ConfigurationBuilder doesn't support async providers.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string?>> LoadSecretValues(string secretArn)
    {
        using var client = new AmazonSecretsManagerClient();
        var response = client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretArn })
            .GetAwaiter()
            .GetResult();

        var json = JsonDocument.Parse(response.SecretString);
        return json.RootElement.EnumerateObject()
            .Select(prop => new KeyValuePair<string, string?>(
                prop.Name.Replace("__", ":"),
                prop.Value.GetString()));
    }
}
