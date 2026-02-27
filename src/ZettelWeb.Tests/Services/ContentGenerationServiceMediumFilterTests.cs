using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Verifies that GenerateContentAsync respects the optional mediums filter,
/// producing only the requested content types (blog, social, or both).
/// </summary>
public class ContentGenerationServiceMediumFilterTests
{
    private static ZettelDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ContentGenerationService CreateService(
        ZettelDbContext db,
        FakeChatClient? chat = null,
        int socialPostCount = 3) =>
        new(db,
            chat ?? new FakeChatClient(),
            new FakeEmbeddingGenerator([0.1f, 0.2f, 0.3f]),
            Options.Create(new ContentGenerationOptions { SocialPostCount = socialPostCount }),
            NullLogger<ContentGenerationService>.Instance);

    private static TopicCluster MakeCluster()
    {
        var noteId = Guid.NewGuid().ToString();
        return new TopicCluster(
            SeedNoteId: noteId,
            Notes: [new Note { Id = noteId, Title = "Test Note", Content = "Test content", CreatedAt = DateTime.UtcNow }],
            TopicSummary: "Decoupling scheduling systems");
    }

    [Fact]
    public async Task GenerateContentAsync_WithBlogOnly_CreatesOnlyBlogPiece()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var cluster = MakeCluster();

        var result = await service.GenerateContentAsync(cluster, ["blog"]);

        Assert.All(result.Pieces, p => Assert.Equal("blog", p.Medium));
        Assert.Single(result.Pieces);
    }

    [Fact]
    public async Task GenerateContentAsync_WithSocialOnly_CreatesOnlySocialPieces()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var cluster = MakeCluster();

        var result = await service.GenerateContentAsync(cluster, ["social"]);

        Assert.All(result.Pieces, p => Assert.Equal("social", p.Medium));
        Assert.NotEmpty(result.Pieces);
    }

    [Fact]
    public async Task GenerateContentAsync_SocialPostCount1_PromptRequestsSinglePost()
    {
        await using var db = CreateDbContext();
        var chat = new FakeChatClient();
        var service = CreateService(db, chat, socialPostCount: 1);
        var cluster = MakeCluster();

        await service.GenerateContentAsync(cluster, ["social"]);

        // Social is the only call when mediums=["social"], so RecordedCalls[0] is the social call
        var userMessage = chat.RecordedCalls[0]
            .First(m => m.Role == ChatRole.User).Text!;
        Assert.Contains("Write 1 social media post", userMessage);
    }

    [Fact]
    public async Task GenerateContentAsync_SocialPostCount5_PromptRequestsFivePosts()
    {
        await using var db = CreateDbContext();
        var chat = new FakeChatClient();
        var service = CreateService(db, chat, socialPostCount: 5);
        var cluster = MakeCluster();

        await service.GenerateContentAsync(cluster, ["social"]);

        var userMessage = chat.RecordedCalls[0]
            .First(m => m.Role == ChatRole.User).Text!;
        Assert.Contains("Write 5 social media posts", userMessage);
    }

    [Fact]
    public async Task GenerateContentAsync_WithNullMediums_CreatesBothBlogAndSocialPieces()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var cluster = MakeCluster();

        var result = await service.GenerateContentAsync(cluster, null);

        Assert.Contains(result.Pieces, p => p.Medium == "blog");
        Assert.Contains(result.Pieces, p => p.Medium == "social");
    }
}
