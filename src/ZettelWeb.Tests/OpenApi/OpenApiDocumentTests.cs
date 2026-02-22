using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.OpenApi;

public class OpenApiDocumentTests : IClassFixture<OpenApiDocumentTests.TestApp>
{
    private readonly HttpClient _client;

    public OpenApiDocumentTests(TestApp app)
    {
        _client = app.CreateClient();
    }

    [Fact]
    public async Task OpenApiEndpoint_Returns200WithValidDocument()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Verify it's a valid OpenAPI 3.x document
        Assert.True(doc.RootElement.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.", version.GetString());

        // Verify document metadata
        var info = doc.RootElement.GetProperty("info");
        Assert.Equal("ZettelWeb API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_ContainsAllNotesEndpoints()
    {
        var paths = await GetPathsAsync();

        Assert.True(paths.TryGetProperty("/api/notes", out _), "Missing /api/notes");
        Assert.True(paths.TryGetProperty("/api/notes/{id}", out _), "Missing /api/notes/{id}");
        Assert.True(paths.TryGetProperty("/api/notes/inbox", out _), "Missing /api/notes/inbox");
        Assert.True(paths.TryGetProperty("/api/notes/inbox/count", out _), "Missing /api/notes/inbox/count");
        Assert.True(paths.TryGetProperty("/api/notes/re-embed", out _), "Missing /api/notes/re-embed");
        Assert.True(paths.TryGetProperty("/api/notes/discover", out _), "Missing /api/notes/discover");
        Assert.True(paths.TryGetProperty("/api/notes/search-titles", out _), "Missing /api/notes/search-titles");
        Assert.True(paths.TryGetProperty("/api/notes/check-duplicate", out _), "Missing /api/notes/check-duplicate");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/promote", out _), "Missing /api/notes/{id}/promote");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/related", out _), "Missing /api/notes/{id}/related");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/backlinks", out _), "Missing /api/notes/{id}/backlinks");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/suggested-tags", out _), "Missing /api/notes/{id}/suggested-tags");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/versions", out _), "Missing /api/notes/{id}/versions");
        Assert.True(paths.TryGetProperty("/api/notes/{id}/versions/{versionId}", out _), "Missing /api/notes/{id}/versions/{versionId}");
        Assert.True(paths.TryGetProperty("/api/notes/{fleetingId}/merge/{targetId}", out _), "Missing /api/notes/{fleetingId}/merge/{targetId}");
    }

    [Fact]
    public async Task OpenApiDocument_ContainsAllOtherEndpoints()
    {
        var paths = await GetPathsAsync();

        Assert.True(paths.TryGetProperty("/api/tags", out _), "Missing /api/tags");
        Assert.True(paths.TryGetProperty("/api/search", out _), "Missing /api/search");
        Assert.True(paths.TryGetProperty("/api/graph", out _), "Missing /api/graph");
        Assert.True(paths.TryGetProperty("/api/discovery", out _), "Missing /api/discovery");
        Assert.True(paths.TryGetProperty("/api/import", out _), "Missing /api/import");
        Assert.True(paths.TryGetProperty("/api/export", out _), "Missing /api/export");
        Assert.True(paths.TryGetProperty("/api/capture/email", out _), "Missing /api/capture/email");
        Assert.True(paths.TryGetProperty("/api/capture/telegram", out _), "Missing /api/capture/telegram");
    }

    [Fact]
    public async Task OpenApiDocument_NoteSchema_ExcludesJsonIgnoredProperties()
    {
        var doc = await GetDocumentAsync();
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        // Find the Note schema
        Assert.True(schemas.TryGetProperty("Note", out var noteSchema), "Missing Note schema");
        var properties = noteSchema.GetProperty("properties");

        // These should be excluded by [JsonIgnore]
        Assert.False(properties.TryGetProperty("embedding", out _), "embedding should be excluded");
        Assert.False(properties.TryGetProperty("enrichmentJson", out _), "enrichmentJson should be excluded");
        Assert.False(properties.TryGetProperty("embedError", out _), "embedError should be excluded");
        Assert.False(properties.TryGetProperty("embeddingModel", out _), "embeddingModel should be excluded");
        Assert.False(properties.TryGetProperty("embedRetryCount", out _), "embedRetryCount should be excluded");
        Assert.False(properties.TryGetProperty("embedUpdatedAt", out _), "embedUpdatedAt should be excluded");
        Assert.False(properties.TryGetProperty("enrichRetryCount", out _), "enrichRetryCount should be excluded");
        Assert.False(properties.TryGetProperty("versions", out _), "versions should be excluded");

        // These should be present
        Assert.True(properties.TryGetProperty("id", out _), "id should be present");
        Assert.True(properties.TryGetProperty("title", out _), "title should be present");
        Assert.True(properties.TryGetProperty("content", out _), "content should be present");
        Assert.True(properties.TryGetProperty("status", out _), "status should be present");
        Assert.True(properties.TryGetProperty("noteType", out _), "noteType should be present");
        Assert.True(properties.TryGetProperty("tags", out _), "tags should be present");
    }

    [Fact]
    public async Task OpenApiDocument_NotesPath_HasCorrectHttpMethods()
    {
        var paths = await GetPathsAsync();

        // /api/notes should have GET and POST
        var notesPath = paths.GetProperty("/api/notes");
        Assert.True(notesPath.TryGetProperty("get", out _), "/api/notes should have GET");
        Assert.True(notesPath.TryGetProperty("post", out _), "/api/notes should have POST");

        // /api/notes/{id} should have GET, PUT, and DELETE
        var noteByIdPath = paths.GetProperty("/api/notes/{id}");
        Assert.True(noteByIdPath.TryGetProperty("get", out _), "/api/notes/{id} should have GET");
        Assert.True(noteByIdPath.TryGetProperty("put", out _), "/api/notes/{id} should have PUT");
        Assert.True(noteByIdPath.TryGetProperty("delete", out _), "/api/notes/{id} should have DELETE");
    }

    private async Task<JsonElement> GetPathsAsync()
    {
        var doc = await GetDocumentAsync();
        return doc.RootElement.GetProperty("paths");
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    public class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres;

        public TestApp()
        {
            _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder("pgvector/pgvector:pg17")
                .Build();
        }

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();
        }

        public new async Task DisposeAsync()
        {
            await base.DisposeAsync();
            await _postgres.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                _postgres.GetConnectionString());
            builder.UseSetting("Embedding:Provider", "openai");
            builder.UseSetting("Embedding:ApiKey", "sk-test");
            builder.UseSetting("Embedding:Model", "fake-model");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                    new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f, 0.3f }));
            });
        }
    }
}
