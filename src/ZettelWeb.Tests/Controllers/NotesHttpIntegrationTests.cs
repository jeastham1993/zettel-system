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
/// HTTP integration tests using WebApplicationFactory with a real PostgreSQL
/// Testcontainers database and fake embedding generator.
/// Tests exercise the full ASP.NET Core pipeline: routing, model binding,
/// controllers, services, and EF Core against a real database.
/// </summary>
public class NotesHttpIntegrationTests : IClassFixture<NotesHttpIntegrationTests.TestApp>
{
    private readonly HttpClient _client;

    public NotesHttpIntegrationTests(TestApp app)
    {
        _client = app.CreateClient();
    }

    private async Task<NoteResponse> CreateNoteAsync(string title, string content)
    {
        // Ensure unique timestamp-based IDs by waiting if needed
        await Task.Delay(1100);
        var response = await _client.PostAsJsonAsync("/api/notes",
            new { title, content });
        response.EnsureSuccessStatusCode();
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        return note!;
    }

    [Fact]
    public async Task POST_Notes_Returns201WithNote()
    {
        await Task.Delay(1100);
        var response = await _client.PostAsJsonAsync("/api/notes",
            new { title = "HTTP Test", content = "Created via HTTP" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        Assert.Equal("HTTP Test", note.Title);
        Assert.Equal("Created via HTTP", note.Content);
        Assert.NotNull(note.Id);
    }

    [Fact]
    public async Task GET_NotesById_Returns200WhenFound()
    {
        var created = await CreateNoteAsync("Get Test", "Body");

        var response = await _client.GetAsync($"/api/notes/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.Equal("Get Test", note!.Title);
    }

    [Fact]
    public async Task GET_NotesById_Returns404WhenNotFound()
    {
        var response = await _client.GetAsync("/api/notes/99999999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GET_Notes_Returns200WithList()
    {
        var response = await _client.GetAsync("/api/notes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedNoteResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
    }

    [Fact]
    public async Task PUT_Notes_Returns200WithUpdatedNote()
    {
        var created = await CreateNoteAsync("Before Update", "Old");

        var response = await _client.PutAsJsonAsync($"/api/notes/{created.Id}",
            new { title = "After Update", content = "New" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.Equal("After Update", note!.Title);
        Assert.Equal("New", note.Content);
    }

    [Fact]
    public async Task PUT_Notes_Returns404WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync("/api/notes/99999999999999",
            new { title = "T", content = "C" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Notes_Returns204WhenFound()
    {
        var created = await CreateNoteAsync("To Delete", "C");

        var response = await _client.DeleteAsync($"/api/notes/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Notes_Returns404WhenNotFound()
    {
        var response = await _client.DeleteAsync("/api/notes/99999999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Test Infrastructure ─────────────────────────────────

    private record NoteResponse(string Id, string Title, string Content);
    private record PagedNoteResponse(List<NoteResponse> Items, int TotalCount);

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
