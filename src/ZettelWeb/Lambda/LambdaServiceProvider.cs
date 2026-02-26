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
using ZettelWeb.Services.Publishing;

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

        // ── Chat client (for ContentScheduleHandler) ─────────────
        var cgProvider = configuration["ContentGeneration:Provider"] ?? "bedrock";
        var cgModel = configuration["ContentGeneration:Model"]
            ?? "anthropic.claude-3-5-sonnet-20241022-v2:0";

        if (string.Equals(cgProvider, "bedrock", StringComparison.OrdinalIgnoreCase))
        {
            var cgRegion = configuration["ContentGeneration:Region"];
            var bedrockClient = !string.IsNullOrEmpty(cgRegion)
                ? new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(cgRegion))
                : new AmazonBedrockRuntimeClient();
            services.AddSingleton<IChatClient>(bedrockClient.AsIChatClient(cgModel));
        }
        else
        {
            var cgApiKey = configuration["ContentGeneration:ApiKey"] ?? "";
            services.AddSingleton<IChatClient>(
                new OpenAIClient(cgApiKey).GetChatClient(cgModel).AsIChatClient());
        }

        // ── Services ────────────────────────────────────────────
        services.AddScoped<INoteService>(sp =>
            new NoteService(
                sp.GetRequiredService<ZettelDbContext>(),
                sp.GetRequiredService<IEmbeddingQueue>(),
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<ILogger<NoteService>>()));

        services.AddScoped<ITopicDiscoveryService, TopicDiscoveryService>();
        services.Configure<TopicDiscoveryOptions>(
            configuration.GetSection("ContentGenerator:TopicDiscovery"));

        services.AddScoped<IContentGenerationService, ContentGenerationService>();
        services.Configure<ContentGenerationOptions>(
            configuration.GetSection(ContentGenerationOptions.SectionName));

        services.Configure<PublishingOptions>(
            configuration.GetSection(PublishingOptions.SectionName));

        services.AddScoped<CaptureService>();
        services.Configure<CaptureConfig>(configuration.GetSection("Capture"));

        services.AddHttpClient("GitHub", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient("Publer", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddKeyedScoped<IPublishingService, GitHubPublishingService>("blog");
        services.AddKeyedScoped<IPublishingService, PublerPublishingService>("social");

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
