using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Tests.Services;

public class ResearchModelsTests
{
    private ZettelDbContext CreateDb() => new(new DbContextOptionsBuilder<ZettelDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ResearchAgenda_CanBeSavedAndRetrieved()
    {
        await using var db = CreateDb();
        db.ResearchAgendas.Add(new ResearchAgenda { Id = "20260226120000001", TriggeredFromNoteId = "note1" });
        await db.SaveChangesAsync();
        var saved = await db.ResearchAgendas.FindAsync("20260226120000001");
        Assert.NotNull(saved);
        Assert.Equal("note1", saved.TriggeredFromNoteId);
        Assert.Equal(ResearchAgendaStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task ResearchTask_RequiresAgendaId()
    {
        await using var db = CreateDb();
        var agenda = new ResearchAgenda { Id = "20260226120000002" };
        db.ResearchAgendas.Add(agenda);
        var task = new ResearchTask { Id = "20260226120000003", AgendaId = agenda.Id, Query = "rust async", Motivation = "because" };
        db.ResearchTasks.Add(task);
        await db.SaveChangesAsync();
        var saved = await db.ResearchTasks.FindAsync("20260226120000003");
        Assert.Equal(agenda.Id, saved!.AgendaId);
    }

    [Fact]
    public async Task ResearchFinding_HasRestrictDeleteConfigured()
    {
        // I2: Verifies that findings are NOT silently deleted when a task is removed.
        // The EF Core InMemory provider does not enforce FK constraint violations at the
        // SQL level, so we verify the schema intent via the model metadata.
        // In PostgreSQL the Restrict behaviour raises a FK violation error on task delete.
        await using var db = CreateDb();

        var findingEntityType = db.Model.FindEntityType(typeof(ResearchFinding))!;
        var taskFk = findingEntityType.GetForeignKeys()
            .Single(fk => fk.Properties.Any(p => p.Name == "TaskId"));

        Assert.Equal(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict, taskFk.DeleteBehavior);
    }

    [Fact]
    public async Task ResearchFinding_SimilarNoteIds_StoredAsJsonb()
    {
        await using var db = CreateDb();
        var agenda = new ResearchAgenda { Id = "20260226120000007" };
        db.ResearchAgendas.Add(agenda);
        var task = new ResearchTask { Id = "20260226120000008", AgendaId = agenda.Id, Query = "q", Motivation = "m" };
        db.ResearchTasks.Add(task);
        var finding = new ResearchFinding
        {
            Id = "20260226120000009",
            TaskId = task.Id,
            Title = "T",
            Synthesis = "S",
            SourceUrl = "https://example.com",
            SimilarNoteIds = new List<string> { "note1", "note2" }
        };
        db.ResearchFindings.Add(finding);
        await db.SaveChangesAsync();

        var saved = await db.ResearchFindings.FindAsync("20260226120000009");
        Assert.Equal(2, saved!.SimilarNoteIds.Count);
        Assert.Contains("note1", saved.SimilarNoteIds);
    }
}
