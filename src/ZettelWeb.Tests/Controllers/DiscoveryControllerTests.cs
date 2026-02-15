using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Controllers;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Controllers;

public class DiscoveryControllerTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task Discover_RandomMode_ReturnsOk()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Old", Content = "C",
            UpdatedAt = DateTime.UtcNow.AddDays(-45)
        });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);
        var controller = new DiscoveryController(service);

        var result = await controller.Discover("random");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var notes = Assert.IsAssignableFrom<IReadOnlyList<Note>>(okResult.Value);
        Assert.Single(notes);
    }

    [Fact]
    public async Task Discover_OrphansMode_ReturnsOrphanNotes()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Orphan", Content = "Plain text"
        });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);
        var controller = new DiscoveryController(service);

        var result = await controller.Discover("orphans");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var notes = Assert.IsAssignableFrom<IReadOnlyList<Note>>(okResult.Value);
        Assert.Single(notes);
    }

    [Fact]
    public async Task Discover_TodayMode_ReturnsNotesFromToday()
    {
        await using var context = CreateDbContext();
        context.Notes.Add(new Note
        {
            Id = "20260213120001", Title = "Today", Content = "C",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new DiscoveryService(context);
        var controller = new DiscoveryController(service);

        var result = await controller.Discover("today");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var notes = Assert.IsAssignableFrom<IReadOnlyList<Note>>(okResult.Value);
        Assert.Single(notes);
    }

    [Fact]
    public async Task Discover_UnknownMode_DefaultsToRandom()
    {
        await using var context = CreateDbContext();
        var service = new DiscoveryService(context);
        var controller = new DiscoveryController(service);

        var result = await controller.Discover("unknown");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Discover_DefaultsToRandomMode()
    {
        await using var context = CreateDbContext();
        var service = new DiscoveryService(context);
        var controller = new DiscoveryController(service);

        var result = await controller.Discover();

        Assert.IsType<OkObjectResult>(result);
    }
}
