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
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Health;
using ZettelWeb.Models;
using ZettelWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IGraphService, GraphService>();
builder.Services.Configure<CaptureConfig>(builder.Configuration.GetSection("Capture"));
builder.Services.AddScoped<CaptureService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
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
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new OllamaApiClient(new Uri(ollamaUri), embeddingModel));
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

builder.Services.AddHostedService<EmbeddingBackgroundService>();
builder.Services.AddHttpClient("Enrichment");
builder.Services.AddHostedService<EnrichmentBackgroundService>();

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
    db.Database.Migrate();

    // HNSW index for fast cosine similarity searches on embeddings.
    // This depends on runtime config (embedding model dimensions vary), so it
    // can't live in a static migration. The column is real[] so we cast to vector.
    var dimensions = app.Configuration.GetValue<int>("Embedding:Dimensions");
    if (dimensions > 0 && dimensions <= 4096)
    {
        db.Database.ExecuteSqlRaw(
            $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
            $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
            $"WHERE \"Embedding\" IS NOT NULL;");
    }
}

app.UseCors();
app.UseRateLimiter();
app.MapControllers();
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
