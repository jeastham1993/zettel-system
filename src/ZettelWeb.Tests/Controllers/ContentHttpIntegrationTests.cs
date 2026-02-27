using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services.Publishing;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

/// <summary>
/// HTTP integration tests for the content generation and voice configuration endpoints.
/// Uses WebApplicationFactory with a real PostgreSQL Testcontainer and fake LLM clients.
/// </summary>
public class ContentHttpIntegrationTests : IClassFixture<ContentHttpIntegrationTests.TestApp>
{
    private readonly TestApp _app;
    private readonly HttpClient _client;

    public ContentHttpIntegrationTests(TestApp app)
    {
        _app = app;
        _client = app.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Inserts a ContentGeneration with one blog piece directly via EF Core and returns its ID.</summary>
    private async Task<string> SeedGenerationAsync(GenerationStatus status = GenerationStatus.Generated)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var genId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 4999)}";
        var pieceId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(5000, 9999)}";

        var generation = new ContentGeneration
        {
            Id = genId,
            SeedNoteId = "seed-note-id",
            ClusterNoteIds = ["seed-note-id"],
            TopicSummary = "A test topic",
            Status = status,
            GeneratedAt = DateTime.UtcNow,
        };
        generation.Pieces.Add(new ContentPiece
        {
            Id = pieceId,
            GenerationId = genId,
            Medium = "blog",
            Body = "Test blog body",
            Status = ContentPieceStatus.Draft,
            Sequence = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.ContentGenerations.Add(generation);
        await db.SaveChangesAsync();
        return genId;
    }

    /// <summary>Inserts an approved ContentGeneration directly via EF Core and returns its ID.</summary>
    private Task<string> SeedApprovedGenerationAsync() =>
        SeedGenerationAsync(GenerationStatus.Approved);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> SeedPermanentNoteAsync(string title = "Test Note", string content = "Some content")
    {
        await Task.Delay(1100); // ensure unique timestamp-based ID
        var response = await _client.PostAsJsonAsync("/api/notes", new
        {
            title,
            content,
            status = "Permanent"
        });
        response.EnsureSuccessStatusCode();
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        return note!.Id;
    }

    // ── POST /api/content/generate ────────────────────────────────────────────

    [Fact]
    public async Task POST_Generate_Returns409_WhenNoEligibleNotes()
    {
        // Database is empty at start of this test (isolated fixture),
        // so no permanent notes with completed embeddings exist.
        var response = await _client.PostAsJsonAsync("/api/content/generate", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Voice Examples ─────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_VoiceExamples_Returns201WithExample()
    {
        var response = await _client.PostAsJsonAsync("/api/content/voice/examples", new
        {
            medium = "blog",
            title = "My writing sample",
            content = "This is how I write. I use short sentences. I get to the point.",
            source = "https://myblog.com/example"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var example = await response.Content.ReadFromJsonAsync<VoiceExampleResponse>();
        Assert.NotNull(example);
        Assert.Equal("blog", example.Medium);
        Assert.Equal("My writing sample", example.Title);
    }

    [Fact]
    public async Task GET_VoiceExamples_Returns200WithList()
    {
        var response = await _client.GetAsync("/api/content/voice/examples");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var examples = await response.Content.ReadFromJsonAsync<List<VoiceExampleResponse>>();
        Assert.NotNull(examples);
    }

    [Fact]
    public async Task DELETE_VoiceExamples_Returns204WhenFound()
    {
        // Create one first
        var createResponse = await _client.PostAsJsonAsync("/api/content/voice/examples", new
        {
            medium = "social",
            content = "Quick social post example"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<VoiceExampleResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/content/voice/examples/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DELETE_VoiceExamples_Returns404WhenNotFound()
    {
        var response = await _client.DeleteAsync("/api/content/voice/examples/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Voice Config ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_VoiceConfig_Returns200AndUpserts()
    {
        var response = await _client.PutAsJsonAsync("/api/content/voice/config", new
        {
            medium = "blog",
            styleNotes = "Write concisely. Avoid jargon. Use active voice."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.Content.ReadFromJsonAsync<VoiceConfigResponse>();
        Assert.NotNull(config);
        Assert.Equal("blog", config.Medium);
        Assert.Equal("Write concisely. Avoid jargon. Use active voice.", config.StyleNotes);
    }

    [Fact]
    public async Task GET_VoiceConfig_Returns200WithAllConfigs()
    {
        // Upsert a config first
        await _client.PutAsJsonAsync("/api/content/voice/config", new
        {
            medium = "social",
            styleNotes = "Be punchy."
        });

        var response = await _client.GetAsync("/api/content/voice/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configs = await response.Content.ReadFromJsonAsync<List<VoiceConfigResponse>>();
        Assert.NotNull(configs);
        Assert.Contains(configs, c => c.Medium == "social");
    }

    [Fact]
    public async Task GET_VoiceConfig_FiltersByMedium()
    {
        await _client.PutAsJsonAsync("/api/content/voice/config", new { medium = "blog", styleNotes = "Blog style." });
        await _client.PutAsJsonAsync("/api/content/voice/config", new { medium = "social", styleNotes = "Social style." });

        var response = await _client.GetAsync("/api/content/voice/config?medium=blog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configs = await response.Content.ReadFromJsonAsync<List<VoiceConfigResponse>>();
        Assert.NotNull(configs);
        Assert.All(configs, c => Assert.Equal("blog", c.Medium));
    }

    // ── GET /api/content/generations ─────────────────────────────────────────

    [Fact]
    public async Task GET_Generations_Returns200WithEmptyList()
    {
        var response = await _client.GetAsync("/api/content/generations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedGenerationsResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
    }

    // ── GET /api/content/pieces ───────────────────────────────────────────────

    [Fact]
    public async Task GET_Pieces_Returns200WithEmptyList()
    {
        var response = await _client.GetAsync("/api/content/pieces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GET_Pieces_FiltersByMedium()
    {
        var response = await _client.GetAsync("/api/content/pieces?medium=blog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GET /api/content/schedule ─────────────────────────────────────────────

    [Fact]
    public async Task GET_Schedule_Returns200WithDefaults()
    {
        var response = await _client.GetAsync("/api/content/schedule");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schedule = await response.Content.ReadFromJsonAsync<ScheduleResponse>();
        Assert.NotNull(schedule);
        Assert.NotNull(schedule.Blog);
        Assert.Equal("Monday", schedule.Blog.DayOfWeek);
        Assert.Equal("09:00", schedule.Blog.TimeOfDay);
        Assert.NotNull(schedule.Social);
        Assert.Equal("09:00", schedule.Social.TimeOfDay);
    }

    // ── Approve / Reject ──────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_ApprovePiece_Returns404WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync("/api/content/pieces/doesnotexist/approve", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_RejectPiece_Returns404WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync("/api/content/pieces/doesnotexist/reject", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_ExportPiece_Returns404WhenNotFound()
    {
        var response = await _client.GetAsync("/api/content/pieces/doesnotexist/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /api/content/generations/{id} ─────────────────────────────────

    [Fact]
    public async Task DELETE_Generation_Returns404_WhenNotFound()
    {
        var response = await _client.DeleteAsync("/api/content/generations/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Generation_Returns204_AndRemovesGenerationWithPieces()
    {
        var id = await SeedGenerationAsync();

        var deleteResponse = await _client.DeleteAsync($"/api/content/generations/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/content/generations/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // ── POST /api/content/generations/{id}/regenerate ─────────────────────────

    [Fact]
    public async Task POST_RegenerateGeneration_Returns404_WhenNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/content/generations/doesnotexist/regenerate", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_RegenerateGeneration_Returns409_WhenApproved()
    {
        var id = await SeedApprovedGenerationAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/content/generations/{id}/regenerate", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── POST /api/content/generations/{id}/regenerate/{medium} ────────────────

    [Fact]
    public async Task POST_RegenerateMedium_Returns404_WhenNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/content/generations/doesnotexist/regenerate/blog", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_RegenerateMedium_Returns400_ForInvalidMedium()
    {
        // Use any ID — the medium validation happens before the DB lookup
        var response = await _client.PostAsJsonAsync(
            "/api/content/generations/anyid/regenerate/podcast", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_RegenerateMedium_Returns409_WhenApproved()
    {
        var id = await SeedApprovedGenerationAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/content/generations/{id}/regenerate/blog", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /api/content/pieces/{id}/description ──────────────────────────────

    [Fact]
    public async Task PUT_UpdateDescription_Returns404_WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/content/pieces/doesnotexist/description",
            new { description = "Test description" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_UpdateDescription_Returns204_AndPersists()
    {
        var genId = await SeedGenerationAsync();
        var pieceId = await GetFirstPieceIdAsync(genId);

        var response = await _client.PutAsJsonAsync(
            $"/api/content/pieces/{pieceId}/description",
            new { description = "My updated description" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/content/pieces/{pieceId}");
        var piece = await getResponse.Content.ReadFromJsonAsync<ContentPieceResponse>();
        Assert.Equal("My updated description", piece!.Description);
    }

    // ── PUT /api/content/pieces/{id}/tags ─────────────────────────────────────

    [Fact]
    public async Task PUT_UpdateTags_Returns404_WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/content/pieces/doesnotexist/tags",
            new { tags = new[] { "dotnet" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_UpdateTags_Returns204_AndPersists()
    {
        var genId = await SeedGenerationAsync();
        var pieceId = await GetFirstPieceIdAsync(genId);

        var response = await _client.PutAsJsonAsync(
            $"/api/content/pieces/{pieceId}/tags",
            new { tags = new[] { "dotnet", "testing", "csharp" } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/content/pieces/{pieceId}");
        var piece = await getResponse.Content.ReadFromJsonAsync<ContentPieceResponse>();
        Assert.Equal(["dotnet", "testing", "csharp"], piece!.GeneratedTags);
    }

    // ── POST /api/content/pieces/{id}/send-to-draft ───────────────────────────

    [Fact]
    public async Task POST_SendToDraft_Returns404_WhenNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/content/pieces/doesnotexist/send-to-draft", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_SendToDraft_Returns422_WhenPublishingNotConfigured()
    {
        // Publishing services have no credentials in the test environment
        var genId = await SeedGenerationAsync();
        var pieceId = await GetFirstPieceIdAsync(genId);

        var response = await _client.PostAsJsonAsync(
            $"/api/content/pieces/{pieceId}/send-to-draft", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task POST_SendToDraft_Returns409_WhenAlreadySent()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var genId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 4999)}";
        var pieceId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(5000, 9999)}";

        var generation = new ContentGeneration
        {
            Id = genId,
            SeedNoteId = "seed-note-id",
            ClusterNoteIds = ["seed-note-id"],
            TopicSummary = "A test topic",
            Status = GenerationStatus.Generated,
            GeneratedAt = DateTime.UtcNow,
        };
        generation.Pieces.Add(new ContentPiece
        {
            Id = pieceId,
            GenerationId = genId,
            Medium = "blog",
            Body = "Test blog body",
            Status = ContentPieceStatus.Draft,
            Sequence = 1,
            CreatedAt = DateTime.UtcNow,
            SentToDraftAt = DateTime.UtcNow,
            DraftReference = "https://github.com/test/repo/blob/main/draft.md",
        });
        db.ContentGenerations.Add(generation);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/content/pieces/{pieceId}/send-to-draft", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<string> GetFirstPieceIdAsync(string generationId)
    {
        var response = await _client.GetAsync($"/api/content/generations/{generationId}");
        var generation = await response.Content.ReadFromJsonAsync<GenerationWithPiecesResponse>();
        return generation!.Pieces[0].Id;
    }

    // ── DTOs (private, test-only) ─────────────────────────────────────────────

    private record NoteResponse(string Id, string Title, string Content);

    private record VoiceExampleResponse(string Id, string Medium, string? Title,
        string Content, string? Source, DateTime CreatedAt);

    private record VoiceConfigResponse(string Id, string Medium, string? StyleNotes,
        DateTime UpdatedAt);

    private record GenerationResponse(string Id, string SeedNoteId,
        List<string> ClusterNoteIds, string TopicSummary, string Status,
        DateTime GeneratedAt, DateTime? ReviewedAt);

    private record PagedGenerationsResponse(List<GenerationResponse> Items, int TotalCount);

    private record BlogScheduleResponse(bool Enabled, string DayOfWeek, string TimeOfDay);
    private record SocialScheduleResponse(bool Enabled, string TimeOfDay);
    private record ScheduleResponse(BlogScheduleResponse Blog, SocialScheduleResponse Social);

    private record ContentPieceResponse(
        string Id, string GenerationId, string Medium, string? Title, string Body,
        string Status, int Sequence, DateTime CreatedAt, DateTime? ApprovedAt,
        string? Description, List<string> GeneratedTags, string? EditorFeedback,
        DateTime? SentToDraftAt, string? DraftReference);

    private record GenerationWithPiecesResponse(
        string Id, string SeedNoteId, List<string> ClusterNoteIds, string TopicSummary,
        string Status, DateTime GeneratedAt, DateTime? ReviewedAt,
        List<ContentPieceResponse> Pieces);

    // ── Test Infrastructure ────────────────────────────────────────────────────

    public class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres;

        public TestApp()
        {
            _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("pgvector/pgvector:pg17")
                .Build();
        }

        public async Task InitializeAsync() => await _postgres.StartAsync();

        public new async Task DisposeAsync()
        {
            await base.DisposeAsync();
            await _postgres.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("Embedding:Provider", "openai");
            builder.UseSetting("Embedding:ApiKey", "sk-test");
            builder.UseSetting("Embedding:Model", "fake-model");
            builder.UseSetting("ContentGeneration:Provider", "openai");
            builder.UseSetting("ContentGeneration:ApiKey", "sk-test");
            builder.UseSetting("ContentGeneration:Model", "fake-model");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                    new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f }));

                services.RemoveAll<IChatClient>();
                services.AddScoped<IChatClient>(_ => new FakeChatClient());
            });
        }
    }
}

/// <summary>
/// Integration tests for the send-to-draft endpoint using a configured FakePublishingService.
/// These tests verify the happy path (approved piece succeeds) and the rejection path
/// (non-approved piece returns 422) when the publishing service is available.
/// </summary>
public class SendToDraftWithFakePublishingTests
    : IClassFixture<SendToDraftWithFakePublishingTests.TestAppWithFakePublishing>
{
    private readonly TestAppWithFakePublishing _app;
    private readonly HttpClient _client;

    public SendToDraftWithFakePublishingTests(TestAppWithFakePublishing app)
    {
        _app = app;
        _client = app.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Inserts a ContentGeneration with one blog piece via EF Core and returns the generation ID.</summary>
    private async Task<string> SeedGenerationAsync(ContentPieceStatus pieceStatus = ContentPieceStatus.Draft)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var genId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 4999)}";
        var pieceId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(5000, 9999)}";

        var generation = new ContentGeneration
        {
            Id = genId,
            SeedNoteId = "seed-note-id",
            ClusterNoteIds = ["seed-note-id"],
            TopicSummary = "A test topic",
            Status = GenerationStatus.Generated,
            GeneratedAt = DateTime.UtcNow,
        };
        generation.Pieces.Add(new ContentPiece
        {
            Id = pieceId,
            GenerationId = genId,
            Medium = "blog",
            Body = "Test blog body",
            Status = pieceStatus,
            Sequence = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.ContentGenerations.Add(generation);
        await db.SaveChangesAsync();
        return genId;
    }

    private async Task<string> GetFirstPieceIdAsync(string generationId)
    {
        var response = await _client.GetAsync($"/api/content/generations/{generationId}");
        var generation = await response.Content.ReadFromJsonAsync<GenerationWithPiecesResponse>();
        return generation!.Pieces[0].Id;
    }

    // ── POST /api/content/pieces/{id}/send-to-draft ───────────────────────────

    [Fact]
    public async Task POST_SendToDraft_Returns200_AndStampsSentAt()
    {
        // Arrange: seed a piece, then approve it via the API
        var genId = await SeedGenerationAsync();
        var pieceId = await GetFirstPieceIdAsync(genId);

        var approveResponse = await _client.PutAsJsonAsync(
            $"/api/content/pieces/{pieceId}/approve", new { });
        approveResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/content/pieces/{pieceId}/send-to-draft", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var piece = await response.Content.ReadFromJsonAsync<ContentPieceResponse>();
        Assert.NotNull(piece);
        Assert.NotNull(piece.SentToDraftAt);
        Assert.NotEmpty(piece.DraftReference!);
        Assert.Equal($"https://fake.draft.example.com/{pieceId}", piece.DraftReference);
    }

    [Fact]
    public async Task POST_SendToDraft_Returns422_WhenPieceNotApproved()
    {
        // Arrange: seed a piece but do NOT approve it (status remains Draft)
        var genId = await SeedGenerationAsync(ContentPieceStatus.Draft);
        var pieceId = await GetFirstPieceIdAsync(genId);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/content/pieces/{pieceId}/send-to-draft", new { });

        // Assert: controller enforces Approved status before calling the publishing service
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── DTOs (private, test-only) ─────────────────────────────────────────────

    private record ContentPieceResponse(
        string Id, string GenerationId, string Medium, string? Title, string Body,
        string Status, int Sequence, DateTime CreatedAt, DateTime? ApprovedAt,
        string? Description, List<string> GeneratedTags, string? EditorFeedback,
        DateTime? SentToDraftAt, string? DraftReference);

    private record GenerationWithPiecesResponse(
        string Id, string SeedNoteId, List<string> ClusterNoteIds, string TopicSummary,
        string Status, DateTime GeneratedAt, DateTime? ReviewedAt,
        List<ContentPieceResponse> Pieces);

    // ── Test Infrastructure ────────────────────────────────────────────────────

    /// <summary>
    /// WebApplicationFactory variant that replaces the real IPublishingService registrations
    /// for "blog" and "social" with FakePublishingService so send-to-draft can succeed in tests.
    /// </summary>
    public class TestAppWithFakePublishing : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres;

        public TestAppWithFakePublishing()
        {
            _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("pgvector/pgvector:pg17")
                .Build();
        }

        public async Task InitializeAsync() => await _postgres.StartAsync();

        public new async Task DisposeAsync()
        {
            await base.DisposeAsync();
            await _postgres.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("Embedding:Provider", "openai");
            builder.UseSetting("Embedding:ApiKey", "sk-test");
            builder.UseSetting("Embedding:Model", "fake-model");
            builder.UseSetting("ContentGeneration:Provider", "openai");
            builder.UseSetting("ContentGeneration:ApiKey", "sk-test");
            builder.UseSetting("ContentGeneration:Model", "fake-model");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                    new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f }));

                services.RemoveAll<IChatClient>();
                services.AddScoped<IChatClient>(_ => new FakeChatClient());

                // Remove the real keyed publishing services and replace with the fake.
                // Keyed descriptors are matched by both ServiceType and ServiceKey.
                var toRemove = services
                    .Where(d => d.ServiceType == typeof(IPublishingService)
                                && (d.ServiceKey is "blog" or "social"))
                    .ToList();
                foreach (var descriptor in toRemove)
                    services.Remove(descriptor);

                services.AddKeyedScoped<IPublishingService, FakePublishingService>("blog");
                services.AddKeyedScoped<IPublishingService, FakePublishingService>("social");
            });
        }
    }
}
