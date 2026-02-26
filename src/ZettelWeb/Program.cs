using System.Threading.RateLimiting;
using Amazon.BedrockRuntime;
using Amazon.SQS;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using OllamaSharp;
using OpenAI;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Health;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Services.Publishing;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ── OpenAPI ────────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new()
        {
            Title = "ZettelWeb API",
            Version = "v1",
            Description = "A self-hosted Zettelkasten knowledge management API with semantic search, " +
                          "multi-method capture, and AI-powered note discovery."
        };
        return Task.CompletedTask;
    });
});

// ── OpenTelemetry ────────────────────────────────────────
var otelEndpoint = builder.Configuration["Otel:Endpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(ZettelTelemetry.ServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(ZettelTelemetry.ServiceName)
            .AddSource("Npgsql")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrEmpty(otelEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(ZettelTelemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrEmpty(otelEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    if (!string.IsNullOrEmpty(otelEndpoint))
    {
        logging.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    }
});

var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ZettelDbContext>(options =>
    options.UseNpgsql(dataSource, o => o.UseVector()));

var searchWeights = builder.Configuration.GetSection("Search").Get<SearchWeights>() ?? new SearchWeights();

builder.Services.AddSingleton<IEmbeddingQueue, ChannelEmbeddingQueue>();
builder.Services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();
builder.Services.AddScoped<INoteService>(sp =>
    new NoteService(
        sp.GetRequiredService<ZettelDbContext>(),
        sp.GetRequiredService<IEmbeddingQueue>(),
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        sp.GetRequiredService<ILogger<NoteService>>()));
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IGraphService, GraphService>();
builder.Services.AddScoped<IKbHealthService, KbHealthService>();
builder.Services.Configure<CaptureConfig>(builder.Configuration.GetSection("Capture"));
builder.Services.AddScoped<CaptureService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.Configure<TopicDiscoveryOptions>(
    builder.Configuration.GetSection("ContentGenerator:TopicDiscovery"));
builder.Services.AddScoped<ITopicDiscoveryService, TopicDiscoveryService>();
builder.Services.AddScoped<ISearchService>(sp =>
    new SearchService(
        sp.GetRequiredService<ZettelDbContext>(),
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        searchWeights,
        sp.GetRequiredService<ILogger<SearchService>>()));

var embeddingProvider = builder.Configuration["Embedding:Provider"] ?? "openai";
var embeddingModel = builder.Configuration["Embedding:Model"] ?? "text-embedding-3-large";

if (string.Equals(embeddingProvider, "ollama", StringComparison.OrdinalIgnoreCase))
{
    var ollamaUri = builder.Configuration["Embedding:OllamaUrl"] ?? "http://localhost:11434";
    var ollamaTimeoutSeconds = builder.Configuration.GetValue("Embedding:HttpTimeoutSeconds", 300);
    var ollamaHttpClient = new HttpClient
    {
        BaseAddress = new Uri(ollamaUri),
        Timeout = TimeSpan.FromSeconds(ollamaTimeoutSeconds)
    };
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new OllamaApiClient(ollamaHttpClient, embeddingModel));
}
else if (string.Equals(embeddingProvider, "bedrock", StringComparison.OrdinalIgnoreCase))
{
    var bedrockRegion = builder.Configuration["Embedding:BedrockRegion"];
    var client = !string.IsNullOrEmpty(bedrockRegion)
        ? new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(bedrockRegion))
        : new AmazonBedrockRuntimeClient();
    var dimensions = builder.Configuration.GetValue<int?>("Embedding:Dimensions");
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        client.AsIEmbeddingGenerator(embeddingModel, dimensions));
}
else
{
    var apiKey = builder.Configuration["Embedding:ApiKey"] ?? "";
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new OpenAIClient(apiKey).GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());
}

// In Lambda, background services are replaced by separate Lambda functions.
// The AWS_LAMBDA_FUNCTION_NAME env var is set automatically by the Lambda runtime.
var isLambda = !string.IsNullOrEmpty(builder.Configuration["AWS_LAMBDA_FUNCTION_NAME"]);

if (!isLambda)
{
    builder.Services.AddHostedService<EmbeddingBackgroundService>();
}

builder.Services.AddHttpClient("Enrichment");

if (!isLambda)
{
    builder.Services.AddHostedService<EnrichmentBackgroundService>();
}

// ── Content Generation LLM (IChatClient) ─────────────────────
builder.Services.Configure<ContentGenerationOptions>(
    builder.Configuration.GetSection(ContentGenerationOptions.SectionName));

var cgProvider = builder.Configuration["ContentGeneration:Provider"] ?? "bedrock";
var cgModel = builder.Configuration["ContentGeneration:Model"]
    ?? "anthropic.claude-3-5-sonnet-20241022-v2:0";

if (string.Equals(cgProvider, "bedrock", StringComparison.OrdinalIgnoreCase))
{
    var cgRegion = builder.Configuration["ContentGeneration:Region"];
    var bedrockClient = !string.IsNullOrEmpty(cgRegion)
        ? new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(cgRegion))
        : new AmazonBedrockRuntimeClient();
    builder.Services.AddSingleton<IChatClient>(bedrockClient.AsIChatClient(cgModel));
}
else
{
    var cgApiKey = builder.Configuration["ContentGeneration:ApiKey"] ?? "";
    builder.Services.AddSingleton<IChatClient>(
        new OpenAIClient(cgApiKey).GetChatClient(cgModel).AsIChatClient());
}

builder.Services.AddScoped<IContentGenerationService, ContentGenerationService>();

// ── Publishing services ────────────────────────────────────
builder.Services.Configure<PublishingOptions>(
    builder.Configuration.GetSection(PublishingOptions.SectionName));
builder.Services.AddHttpClient("GitHub", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("Publer", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddKeyedScoped<IPublishingService, GitHubPublishingService>("blog");
builder.Services.AddKeyedScoped<IPublishingService, PublerPublishingService>("social");

if (!isLambda && string.Equals(
    builder.Configuration["ContentGeneration:Schedule:Enabled"], "true",
    StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<ContentGenerationScheduler>();
}

var sqsQueueUrl = builder.Configuration["Capture:SqsQueueUrl"];
if (!string.IsNullOrEmpty(sqsQueueUrl))
{
    var sqsRegion = builder.Configuration["Capture:SqsRegion"];
    if (!string.IsNullOrEmpty(sqsRegion))
    {
        builder.Services.AddSingleton<IAmazonSQS>(_ =>
            new AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(sqsRegion)));
    }
    else
    {
        builder.Services.AddSingleton<IAmazonSQS, AmazonSQSClient>();
    }
    builder.Services.AddSingleton<SqsPollingBackgroundService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SqsPollingBackgroundService>());
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<EmbeddingHealthCheck>("embedding");

if (!string.IsNullOrEmpty(sqsQueueUrl))
{
    healthChecks.AddCheck<SqsPollingHealthCheck>("sqs-polling");
}

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins is ["*"])
            policy.AllowAnyOrigin();
        else if (corsOrigins.Length > 0)
            policy.WithOrigins(corsOrigins);

        policy.AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("capture", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded", _);
    };
});

var app = builder.Build();

// Migrations and HNSW index creation are handled by the MigrationLambda,
// which is invoked once by Terraform (aws_lambda_invocation) during deployment.
// Running migrations at startup is unsafe in Lambda: multiple concurrent cold starts
// would all attempt to acquire the migration lock simultaneously.
// For local development and Docker Compose, the MigrationHandler can be invoked
// directly: dotnet run --project ZettelWeb -- --migrate
if (!isLambda)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
    db.Database.Migrate();

    var dimensions = app.Configuration.GetValue<int>("Embedding:Dimensions");
    if (dimensions > 0 && dimensions <= 4096)
    {
#pragma warning disable EF1003 // Intentional raw SQL for DDL — see safety comment in MigrationHandler
        db.Database.ExecuteSqlRaw(
            $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
            $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
            $"WHERE \"Embedding\" IS NOT NULL;");
#pragma warning restore EF1003
    }
}

var publishingOpts = app.Services.GetRequiredService<IOptions<PublishingOptions>>().Value;
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation(
    "Publishing — GitHub configured: {GitHub} (token present: {Token}, owner: '{Owner}', repo: '{Repo}'), Publer configured: {Publer} (key present: {Key}, accounts: {Accounts})",
    publishingOpts.GitHub.IsConfigured,
    !string.IsNullOrEmpty(publishingOpts.GitHub.Token),
    publishingOpts.GitHub.Owner,
    publishingOpts.GitHub.Repo,
    publishingOpts.Publer.IsConfigured,
    !string.IsNullOrEmpty(publishingOpts.Publer.ApiKey),
    publishingOpts.Publer.Accounts.Count);

app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("ZettelWeb API")
           .WithTheme(ScalarTheme.Mars);
});
app.MapHealthChecks("/health", new()
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    data = e.Value.Data
                })
        };
        await JsonSerializer.SerializeAsync(context.Response.Body, result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
});

app.Run();
