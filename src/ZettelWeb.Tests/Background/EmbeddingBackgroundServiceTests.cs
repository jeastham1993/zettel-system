using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Background;

public class EmbeddingBackgroundServiceTests
{
    private static ServiceProvider BuildServiceProvider(string dbName,
        IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ZettelDbContext>(o =>
            o.UseInMemoryDatabase(dbName));
        services.AddSingleton(generator);
        services.AddSingleton<IEmbeddingQueue, ChannelEmbeddingQueue>();
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(int? maxInputChars = null, int? maxRetries = null)
    {
        var dict = new Dictionary<string, string?>();
        if (maxInputChars.HasValue)
            dict["Embedding:MaxInputCharacters"] = maxInputChars.Value.ToString();
        if (maxRetries.HasValue)
            dict["Embedding:MaxRetries"] = maxRetries.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static EmbeddingBackgroundService CreateService(ServiceProvider sp, IConfiguration? config = null)
    {
        return new EmbeddingBackgroundService(
            sp.GetRequiredService<IEmbeddingQueue>(),
            sp,
            NullLogger<EmbeddingBackgroundService>.Instance,
            config ?? BuildConfig());
    }

    private static ZettelDbContext CreateContext(ServiceProvider sp)
    {
        var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
    }

    private static async Task<Note?> FreshLoadAsync(ServiceProvider sp, string noteId)
    {
        var ctx = CreateContext(sp);
        return await ctx.Notes.FindAsync(noteId);
    }

    [Fact]
    public async Task ProcessNoteAsync_SetsStatusToCompletedOnSuccess()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f, 0.2f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260213120000");
        Assert.Equal(EmbedStatus.Completed, note!.EmbedStatus);
        Assert.Equal("fake-model", note.EmbeddingModel);
        Assert.NotNull(note.EmbedUpdatedAt);
        Assert.Null(note.EmbedError);
        Assert.NotNull(note.Embedding);
        Assert.Equal(new float[] { 0.1f, 0.2f }, note.Embedding.ToArray());
    }

    [Fact]
    public async Task ProcessNoteAsync_SetsStatusToFailedOnError()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(
            exception: new HttpRequestException("API down"));
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260213120000");
        Assert.Equal(EmbedStatus.Failed, note!.EmbedStatus);
        Assert.Equal(1, note.EmbedRetryCount);
        Assert.Contains("API down", note.EmbedError);
        Assert.NotNull(note.EmbedUpdatedAt);
    }

    [Fact]
    public async Task ProcessNoteAsync_IncrementsRetryCountOnRepeatedFailure()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(
            exception: new HttpRequestException("Still down"));
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Failed,
            EmbedRetryCount = 2,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        var note = await FreshLoadAsync(sp, "20260213120000");
        Assert.Equal(3, note!.EmbedRetryCount);
    }

    [Fact]
    public async Task ProcessNoteAsync_SkipsMissingNote()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var service = CreateService(sp);

        // Should not throw
        await service.ProcessNoteAsync("nonexistent", CancellationToken.None);

        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task ProcessNoteAsync_CombinesTitleAndContentForEmbedding()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "My Title",
            Content = "My content body",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal("My Title\n\nMy content body", generator.LastInput);
    }

    [Fact]
    public async Task RecoverProcessingNotesAsync_ResetsToPending()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120001",
            Title = "Stuck Note",
            Content = "Content",
            EmbedStatus = EmbedStatus.Processing,
        });
        context.Notes.Add(new Note
        {
            Id = "20260213120002",
            Title = "Completed Note",
            Content = "Content",
            EmbedStatus = EmbedStatus.Completed,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        await service.RecoverProcessingNotesAsync(CancellationToken.None);

        var stuck = await FreshLoadAsync(sp, "20260213120001");
        var completed = await FreshLoadAsync(sp, "20260213120002");
        Assert.Equal(EmbedStatus.Pending, stuck!.EmbedStatus);
        Assert.Equal(EmbedStatus.Completed, completed!.EmbedStatus);
    }

    [Fact]
    public async Task GetPendingNoteIdsAsync_ReturnsPendingFailedAndStale()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.AddRange(
            new Note { Id = "note_pending", Title = "T", Content = "C", EmbedStatus = EmbedStatus.Pending },
            new Note { Id = "note_failed", Title = "T", Content = "C", EmbedStatus = EmbedStatus.Failed },
            new Note { Id = "note_stale", Title = "T", Content = "C", EmbedStatus = EmbedStatus.Stale },
            new Note { Id = "note_completed", Title = "T", Content = "C", EmbedStatus = EmbedStatus.Completed },
            new Note { Id = "note_processing", Title = "T", Content = "C", EmbedStatus = EmbedStatus.Processing }
        );
        await context.SaveChangesAsync();

        var service = CreateService(sp);

        var ids = await service.GetPendingNoteIdsAsync(CancellationToken.None);

        Assert.Contains("note_pending", ids);
        Assert.Contains("note_failed", ids);
        Assert.Contains("note_stale", ids);
        Assert.DoesNotContain("note_completed", ids);
        Assert.DoesNotContain("note_processing", ids);
    }

    [Fact]
    public async Task ProcessNoteAsync_SetsProcessingBeforeCallingProvider()
    {
        var dbName = Guid.NewGuid().ToString();
        EmbedStatus? statusDuringCall = null;

        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f },
            onCall: async (sp) =>
            {
                // Use a fresh context to read the persisted state
                var scope = sp.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
                var n = await ctx.Notes.FindAsync("20260213120000");
                statusDuringCall = n?.EmbedStatus;
            });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test",
            Content = "Content",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        generator.ServiceProvider = sp;

        var service = CreateService(sp);

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal(EmbedStatus.Processing, statusDuringCall);
    }

    [Fact]
    public async Task ProcessNoteAsync_SkipsNoteExceedingMaxRetries()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Failed,
            EmbedRetryCount = 3,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp, BuildConfig(maxRetries: 3));

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal(0, generator.CallCount);
        var note = await FreshLoadAsync(sp, "20260213120000");
        Assert.Equal(EmbedStatus.Failed, note!.EmbedStatus);
        Assert.Equal(3, note.EmbedRetryCount);
    }

    [Fact]
    public async Task ProcessNoteAsync_RetriesWhenBelowMaxRetries()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Test Note",
            Content = "Some content",
            EmbedStatus = EmbedStatus.Failed,
            EmbedRetryCount = 2,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp, BuildConfig(maxRetries: 3));

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal(1, generator.CallCount);
        var note = await FreshLoadAsync(sp, "20260213120000");
        Assert.Equal(EmbedStatus.Completed, note!.EmbedStatus);
    }

    [Fact]
    public async Task ProcessNoteAsync_TruncatesToConfiguredLimit()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        var longContent = new string('x', 5000);
        var context = CreateContext(sp);
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = longContent,
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp, BuildConfig(maxInputChars: 100));

        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal(100, generator.LastInput!.Length);
    }

}
