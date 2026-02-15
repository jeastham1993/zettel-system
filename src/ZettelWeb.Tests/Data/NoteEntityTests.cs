using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Tests.Data;

public class NoteEntityTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task CanPersistAndRetrieveNote()
    {
        await using var context = CreateDbContext();

        var note = new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some markdown content",
        };

        context.Notes.Add(note);
        await context.SaveChangesAsync();

        var saved = await context.Notes.FindAsync("20260213120000");

        Assert.NotNull(saved);
        Assert.Equal("Test Note", saved.Title);
        Assert.Equal("Some markdown content", saved.Content);
    }

    [Fact]
    public async Task NoteHasDefaultEmbedStatusOfPending()
    {
        await using var context = CreateDbContext();

        var note = new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Content here",
        };

        context.Notes.Add(note);
        await context.SaveChangesAsync();

        var saved = await context.Notes.FindAsync("20260213120000");

        Assert.NotNull(saved);
        Assert.Equal(EmbedStatus.Pending, saved.EmbedStatus);
    }

    [Fact]
    public async Task NoteHasTimestampsSetOnCreation()
    {
        await using var context = CreateDbContext();

        var note = new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Content",
        };

        context.Notes.Add(note);
        await context.SaveChangesAsync();

        var saved = await context.Notes.FindAsync("20260213120000");

        Assert.NotNull(saved);
        Assert.True(saved.CreatedAt <= DateTime.UtcNow);
        Assert.True(saved.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task NoteRequiresTitle()
    {
        await using var context = CreateDbContext();

        var note = new Note
        {
            Id = "20260213120000",
            Title = null!,
            Content = "Content",
        };

        context.Notes.Add(note);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task NoteRequiresContent()
    {
        await using var context = CreateDbContext();

        var note = new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = null!,
        };

        context.Notes.Add(note);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }
}
