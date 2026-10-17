
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
builder.Services.AddSingleton<IUrlSafetyChecker, UrlSafetyChecker>();
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
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<ISearchService>(sp =>
    new SearchService(
        sp.GetRequiredService<ZettelDbContext>(),
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        searchWeights,
        sp.GetRequiredService<ILogger<SearchService>>()));

builder.Services.AddScoped<IChatService>(sp =>
    new ChatService(
        sp.GetRequiredService<ZettelDbContext>(),
        sp.GetRequiredService<ISearchService>(),
        sp.GetRequiredService<IChatClient>(),
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        sp.GetRequiredService<ILogger<ChatService>>()));

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
else
{
    var apiKey = builder.Configuration["Embedding:ApiKey"] ?? "";
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new OpenAIClient(apiKey).GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());
}

builder.Services.AddHostedService<EmbeddingBackgroundService>();

builder.Services.AddHttpClient("Enrichment");
builder.Services.Configure<ResearchOptions>(builder.Configuration.GetSection(ResearchOptions.SectionName));
builder.Services.AddHttpClient("BraveSearch", c => c.Timeout = TimeSpan.FromSeconds(15));  // C3: explicit timeout
builder.Services.AddHttpClient("Arxiv", c => c.Timeout = TimeSpan.FromSeconds(15));         // C3: explicit timeout
builder.Services.AddSingleton<IWebSearchClient, BraveSearchClient>();
builder.Services.AddSingleton<IArxivClient, ArxivApiClient>();
builder.Services.AddScoped<IResearchAgentService, ResearchAgentService>();
builder.Services.AddSingleton<IResearchExecutionQueue, ChannelResearchExecutionQueue>();
builder.Services.AddHostedService<ResearchExecutionBackgroundService>();
builder.Services.AddHostedService<EnrichmentBackgroundService>();

// ── LLM chat client (IChatClient) — used by Chat, Research, and KB Health. ──
// Note: config keys remain under the ContentGeneration: section for
// backward compatibility with deployed environment configuration.
// Providers: "ollama" (local, default — uses Ollama's OpenAI-compatible
// endpoint; URL defaults to Embedding:OllamaUrl) or "openai" (cloud,
// requires ContentGeneration:ApiKey).
var cgProvider = builder.Configuration["ContentGeneration:Provider"] ?? "ollama";
var cgModel = builder.Configuration["ContentGeneration:Model"] ?? "llama3.1";

if (string.Equals(cgProvider, "bedrock", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "ContentGeneration:Provider 'bedrock' is no longer supported (AWS support was removed). " +
        "Use 'ollama' (local) or 'openai' instead.");
}

if (string.Equals(cgProvider, "ollama", StringComparison.OrdinalIgnoreCase))
{
    var chatOllamaUrl =
        (builder.Configuration["ContentGeneration:OllamaUrl"] is { Length: > 0 } cgOllamaUrl ? cgOllamaUrl : null)
        ?? (builder.Configuration["Embedding:OllamaUrl"] is { Length: > 0 } embOllamaUrl ? embOllamaUrl : null)
        ?? "http://localhost:11434";
    builder.Services.AddSingleton<IChatClient>(
        new OpenAIClient(
            new System.ClientModel.ApiKeyCredential("ollama"),
            new OpenAIClientOptions { Endpoint = new Uri(new Uri(chatOllamaUrl), "/v1") })
        .GetChatClient(cgModel).AsIChatClient());
}
else
{
    var cgApiKey = builder.Configuration["ContentGeneration:ApiKey"];
    if (string.IsNullOrWhiteSpace(cgApiKey))
    {
        throw new InvalidOperationException(
            "ContentGeneration:ApiKey is required when the chat LLM provider is 'openai'. " +
            "Set CONTENTGENERATION__APIKEY, or switch to the local Ollama provider with " +
            "CONTENTGENERATION__PROVIDER=ollama (pull a chat model first, e.g. 'ollama pull llama3.1').");
    }
    builder.Services.AddSingleton<IChatClient>(
        new OpenAIClient(cgApiKey).GetChatClient(cgModel).AsIChatClient());
}

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<EmbeddingHealthCheck>("embedding");

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

var app = builder.Build();

// Apply pending EF migrations at startup, then ensure the HNSW index exists.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
    db.Database.Migrate();

    var dimensions = app.Configuration.GetValue<int>("Embedding:Dimensions");
    if (dimensions > 0 && dimensions <= 4096)
    {
#pragma warning disable EF1003 // Intentional raw SQL for DDL
        db.Database.ExecuteSqlRaw(
            $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
            $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
            $"WHERE \"Embedding\" IS NOT NULL;");
#pragma warning restore EF1003
    }
}

app.UseCors();

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
