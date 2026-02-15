using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class TagServiceTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task CreateNote_WithTags_PersistsTags()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        var note = await service.CreateAsync(
            "Title", "Content", new[] { "rust", "programming" });

        var saved = await context.Notes
            .Include(n => n.Tags)
            .FirstAsync(n => n.Id == note.Id);

        Assert.Equal(2, saved.Tags.Count);
        Assert.Contains(saved.Tags, t => t.Tag == "rust");
        Assert.Contains(saved.Tags, t => t.Tag == "programming");
    }

    [Fact]
    public async Task CreateNote_WithNoTags_HasEmptyTagsList()
    {
        await using var context = CreateDbContext();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        var note = await service.CreateAsync("Title", "Content");

        var saved = await context.Notes
            .Include(n => n.Tags)
            .FirstAsync(n => n.Id == note.Id);

        Assert.Empty(saved.Tags);
    }

    [Fact]
    public async Task UpdateNote_WithTags_ReplacesExistingTags()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Content",
            Tags = new List<NoteTag>
            {
                new() { NoteId = "20260213120000", Tag = "old-tag" }
            }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        await service.UpdateAsync(
            "20260213120000", "Title", "Content",
            new[] { "new-tag-1", "new-tag-2" });

        var saved = await context.Notes
            .Include(n => n.Tags)
            .FirstAsync(n => n.Id == "20260213120000");

        Assert.Equal(2, saved.Tags.Count);
        Assert.DoesNotContain(saved.Tags, t => t.Tag == "old-tag");
        Assert.Contains(saved.Tags, t => t.Tag == "new-tag-1");
    }

    [Fact]
    public async Task GetByIdAsync_IncludesTags()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Content",
            Tags = new List<NoteTag>
            {
                new() { NoteId = "20260213120000", Tag = "tagged" }
            }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        var note = await service.GetByIdAsync("20260213120000");

        Assert.NotNull(note);
        Assert.Single(note.Tags);
        Assert.Equal("tagged", note.Tags[0].Tag);
    }

    [Fact]
    public async Task SearchTagsAsync_ReturnsMatchingTags()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Content",
            Tags = new List<NoteTag>
            {
                new() { NoteId = "20260213120000", Tag = "rust" },
                new() { NoteId = "20260213120000", Tag = "ruby" },
                new() { NoteId = "20260213120000", Tag = "python" },
            }
        });
        await context.SaveChangesAsync();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        var tags = await service.SearchTagsAsync("ru");

        Assert.Equal(2, tags.Count);
        Assert.Contains("rust", tags);
        Assert.Contains("ruby", tags);
    }

    [Fact]
    public async Task SearchTagsAsync_ReturnsDistinctTags()
    {
        await using var context = CreateDbContext();
        context.Notes.AddRange(
            new Note
            {
                Id = "20260213120001",
                Title = "Note 1",
                Content = "C1",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120001", Tag = "rust" }
                }
            },
            new Note
            {
                Id = "20260213120002",
                Title = "Note 2",
                Content = "C2",
                Tags = new List<NoteTag>
                {
                    new() { NoteId = "20260213120002", Tag = "rust" }
                }
            });
        await context.SaveChangesAsync();
        var service = new NoteService(context, new ChannelEmbeddingQueue());

        var tags = await service.SearchTagsAsync("ru");

        Assert.Single(tags);
    }
}
