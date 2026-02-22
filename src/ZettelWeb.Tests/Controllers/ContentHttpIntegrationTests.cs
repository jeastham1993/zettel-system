using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZettelWeb.Models;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Controllers;

/// <summary>
/// HTTP integration tests for the content generation and voice configuration endpoints.
/// Uses WebApplicationFactory with a real PostgreSQL Testcontainer and fake LLM clients.
/// </summary>
public class ContentHttpIntegrationTests : IClassFixture<ContentHttpIntegrationTests.TestApp>
{
    private readonly HttpClient _client;

    public ContentHttpIntegrationTests(TestApp app)
    {
        _client = app.CreateClient();
    }

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
        var response = await _client.PostAsJsonAsync("/api/voice/examples", new
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
        var response = await _client.GetAsync("/api/voice/examples");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var examples = await response.Content.ReadFromJsonAsync<List<VoiceExampleResponse>>();
        Assert.NotNull(examples);
    }

    [Fact]
    public async Task DELETE_VoiceExamples_Returns204WhenFound()
    {
        // Create one first
        var createResponse = await _client.PostAsJsonAsync("/api/voice/examples", new
        {
            medium = "social",
            content = "Quick social post example"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<VoiceExampleResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/voice/examples/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DELETE_VoiceExamples_Returns404WhenNotFound()
    {
        var response = await _client.DeleteAsync("/api/voice/examples/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Voice Config ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_VoiceConfig_Returns200AndUpserts()
    {
        var response = await _client.PutAsJsonAsync("/api/voice/config", new
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
        await _client.PutAsJsonAsync("/api/voice/config", new
        {
            medium = "social",
            styleNotes = "Be punchy."
        });

        var response = await _client.GetAsync("/api/voice/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configs = await response.Content.ReadFromJsonAsync<List<VoiceConfigResponse>>();
        Assert.NotNull(configs);
        Assert.Contains(configs, c => c.Medium == "social");
    }

    [Fact]
    public async Task GET_VoiceConfig_FiltersByMedium()
    {
        await _client.PutAsJsonAsync("/api/voice/config", new { medium = "blog", styleNotes = "Blog style." });
        await _client.PutAsJsonAsync("/api/voice/config", new { medium = "social", styleNotes = "Social style." });

        var response = await _client.GetAsync("/api/voice/config?medium=blog");

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
        Assert.Equal("Monday", schedule.DayOfWeek);
        Assert.Equal("09:00", schedule.TimeOfDay);
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

    private record ScheduleResponse(bool Enabled, string DayOfWeek, string TimeOfDay);

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
