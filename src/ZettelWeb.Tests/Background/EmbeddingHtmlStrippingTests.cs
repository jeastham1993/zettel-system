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

public class EmbeddingHtmlStrippingTests
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

    private static IConfiguration BuildConfig(int? maxInputChars = null)
    {
        var dict = new Dictionary<string, string?>();
        if (maxInputChars.HasValue)
            dict["Embedding:MaxInputCharacters"] = maxInputChars.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static EmbeddingBackgroundService CreateService(
        ServiceProvider sp, IConfiguration? config = null)
    {
        return new EmbeddingBackgroundService(
            sp.GetRequiredService<IEmbeddingQueue>(),
            sp,
            NullLogger<EmbeddingBackgroundService>.Instance,
            config ?? BuildConfig());
    }

    [Fact]
    public async Task ProcessNoteAsync_StripsHtmlFromContentBeforeEmbedding()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "My Title",
            Content = "<p>Hello <b>world</b></p><br>Next line",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);
        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        // HTML tags should be stripped, whitespace collapsed
        Assert.NotNull(generator.LastInput);
        Assert.DoesNotContain("<p>", generator.LastInput);
        Assert.DoesNotContain("<b>", generator.LastInput);
        Assert.DoesNotContain("</p>", generator.LastInput);
        Assert.Contains("Hello", generator.LastInput);
        Assert.Contains("world", generator.LastInput);
        Assert.StartsWith("My Title\n\n", generator.LastInput);
    }

    [Fact]
    public async Task ProcessNoteAsync_CollapsesWhitespaceAfterHtmlStrip()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "<p>  word1  </p>  <p>  word2  </p>",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);
        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        // Multiple spaces should be collapsed to single space
        Assert.NotNull(generator.LastInput);
        Assert.DoesNotContain("  ", generator.LastInput.Replace("Title\n\n", ""));
    }

    [Fact]
    public async Task ProcessNoteAsync_PlainTextContentUnchanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var generator = new FakeEmbeddingGenerator(new float[] { 0.1f });
        using var sp = BuildServiceProvider(dbName, generator);

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        context.Notes.Add(new Note
        {
            Id = "20260213120000",
            Title = "Title",
            Content = "Plain text with no HTML",
            EmbedStatus = EmbedStatus.Pending,
        });
        await context.SaveChangesAsync();

        var service = CreateService(sp);
        await service.ProcessNoteAsync("20260213120000", CancellationToken.None);

        Assert.Equal("Title\n\nPlain text with no HTML", generator.LastInput);
    }
}
