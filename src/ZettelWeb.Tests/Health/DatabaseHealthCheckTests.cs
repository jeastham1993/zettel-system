using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb.Data;
using ZettelWeb.Health;
using ZettelWeb.Models;

namespace ZettelWeb.Tests.Health;

public class DatabaseHealthCheckTests
{
    private ZettelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ZettelDbContext(options);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDbAccessible_ReturnsHealthy()
    {
        await using var db = CreateDbContext();
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsNoteCount()
    {
        await using var db = CreateDbContext();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "A", Content = "C" },
            new Note { Id = "20260213120002", Title = "B", Content = "C" });
        await db.SaveChangesAsync();
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("2", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsEmbeddingStats()
    {
        await using var db = CreateDbContext();
        db.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "A", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "20260213120002", Title = "B", Content = "C", EmbedStatus = EmbedStatus.Pending },
            new Note { Id = "20260213120003", Title = "C", Content = "C", EmbedStatus = EmbedStatus.Failed });
        await db.SaveChangesAsync();
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext());

        Assert.True(result.Data.ContainsKey("total_notes"));
        Assert.Equal(3, result.Data["total_notes"]);
        Assert.Equal(1, result.Data["embedded"]);
        Assert.Equal(1, result.Data["pending"]);
        Assert.Equal(1, result.Data["failed"]);
    }
}
