using System.Threading.RateLimiting;
using Amazon.SQS;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using OllamaSharp;
using OpenAI;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Health;
using ZettelWeb.Models;
using ZettelWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
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
    db.Database.EnsureCreated();

    // Ensure pgvector extension exists (EnsureCreated skips this on existing DBs)
    db.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector;");

    // Revert any vector column back to real[] for EF Core float[] compatibility.
    // The SearchService uses ::vector casts in raw SQL for pgvector operations.
    db.Database.ExecuteSqlRaw("""
        DO $$ BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'Notes' AND column_name = 'Embedding'
                  AND udt_name = 'vector'
            ) THEN
                ALTER TABLE "Notes" ALTER COLUMN "Embedding"
                    TYPE real[] USING "Embedding"::real[];
            END IF;
        END $$;
        """);

    // Migrate schema for existing databases (EnsureCreated is a no-op on existing DBs)
    db.Database.ExecuteSqlRaw("""
        DO $$ BEGIN
            -- Widen Id columns to varchar(21) for timestamp + random suffix IDs
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'Notes' AND column_name = 'Id'
                  AND character_maximum_length < 21
            ) THEN
                ALTER TABLE "Notes" ALTER COLUMN "Id" TYPE character varying(21);
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'NoteTags' AND column_name = 'NoteId'
                  AND character_maximum_length < 21
            ) THEN
                ALTER TABLE "NoteTags" ALTER COLUMN "NoteId" TYPE character varying(21);
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'NoteVersions' AND column_name = 'NoteId'
                  AND character_maximum_length < 21
            ) THEN
                ALTER TABLE "NoteVersions" ALTER COLUMN "NoteId" TYPE character varying(21);
            END IF;
            -- Add Batch 20 (Fleeting Notes) columns
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'Status') THEN
                ALTER TABLE "Notes" ADD COLUMN "Status" character varying(20) NOT NULL DEFAULT 'Permanent';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'Source') THEN
                ALTER TABLE "Notes" ADD COLUMN "Source" character varying(20);
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'EnrichmentJson') THEN
                ALTER TABLE "Notes" ADD COLUMN "EnrichmentJson" text;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'EnrichStatus') THEN
                ALTER TABLE "Notes" ADD COLUMN "EnrichStatus" character varying(20) NOT NULL DEFAULT 'None';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'EnrichRetryCount') THEN
                ALTER TABLE "Notes" ADD COLUMN "EnrichRetryCount" integer NOT NULL DEFAULT 0;
            END IF;
            -- Add Batch 21 (Structure Notes & Sources) columns
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'NoteType') THEN
                ALTER TABLE "Notes" ADD COLUMN "NoteType" character varying(20) NOT NULL DEFAULT 'Regular';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'SourceAuthor') THEN
                ALTER TABLE "Notes" ADD COLUMN "SourceAuthor" text;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'SourceTitle') THEN
                ALTER TABLE "Notes" ADD COLUMN "SourceTitle" text;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'SourceUrl') THEN
                ALTER TABLE "Notes" ADD COLUMN "SourceUrl" text;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'SourceYear') THEN
                ALTER TABLE "Notes" ADD COLUMN "SourceYear" integer;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Notes' AND column_name = 'SourceType') THEN
                ALTER TABLE "Notes" ADD COLUMN "SourceType" character varying(20);
            END IF;
        END $$;
        """);

    // Create NoteVersions table for version history
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "NoteVersions" (
            "Id" SERIAL PRIMARY KEY,
            "NoteId" character varying(21) NOT NULL,
            "Title" text NOT NULL,
            "Content" text NOT NULL,
            "Tags" text,
            "SavedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
            CONSTRAINT "FK_NoteVersions_Notes_NoteId"
                FOREIGN KEY ("NoteId") REFERENCES "Notes"("Id") ON DELETE CASCADE
        );
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_noteversions_noteid
        ON "NoteVersions" ("NoteId");
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_status ON "Notes" ("Status");
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_notetype ON "Notes" ("NoteType");
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_embed_status
        ON "Notes" ("EmbedStatus")
        WHERE "EmbedStatus" IN ('Pending', 'Failed', 'Stale');
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_enrich_status
        ON "Notes" ("EnrichStatus")
        WHERE "EnrichStatus" IN ('Pending', 'Failed');
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_created_at
        ON "Notes" ("CreatedAt" DESC);
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notetags_tag
        ON "NoteTags" ("Tag");
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS idx_notes_fulltext
        ON "Notes" USING GIN (to_tsvector('english', "Title" || ' ' || "Content"));
        """);

    // HNSW index for fast cosine similarity searches on embeddings.
    // The column is real[] so we cast to vector in the index expression.
    // pgvector requires explicit dimensions for HNSW index creation.
    var dimensions = app.Configuration.GetValue<int>("Embedding:Dimensions");
    if (dimensions > 0 && dimensions <= 4096)
    {
        // dimensions is validated as a safe integer, so string interpolation is safe for DDL
        db.Database.ExecuteSqlRaw(
            $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
            $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
            $"WHERE \"Embedding\" IS NOT NULL;");
    }
}

if (app.Environment.IsDevelopment())
    app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
