using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using ZettelWeb.Data;
using ZettelWeb.Models;
using ZettelWeb.Services;
using ZettelWeb.Tests.Fakes;

namespace ZettelWeb.Tests.Services;

public class SearchServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .Build();
        await _postgres.StartAsync();

        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS idx_notes_fulltext
            ON "Notes" USING GIN (to_tsvector('english', "Title" || ' ' || "Content"));
            """);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private ZettelDbContext CreateDbContext()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<ZettelDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), o => o.UseVector())
            .Options;
        return new ZettelDbContext(options);
    }

    private SearchService CreateService(
        ZettelDbContext context,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        SearchWeights? weights = null)
        => new(context, generator, weights ?? DefaultWeights(),
            NullLogger<SearchService>.Instance);

    private static SearchWeights DefaultWeights() => new()
    {
        SemanticWeight = 0.7,
        FullTextWeight = 0.3,
    };

    private static FakeEmbeddingGenerator CreateFakeGenerator(float[]? result = null)
        => new(result ?? new float[] { 0.1f, 0.2f, 0.3f });

    private async Task ClearNotesAsync(ZettelDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "NoteTags" """);
        await context.Database.ExecuteSqlRawAsync("""DELETE FROM "Notes" """);
    }

    // ── Full-Text Search Tests ───────────────────────────────

    [Fact]
    public async Task FullTextSearchAsync_ReturnsMatchingNotes()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note { Id = "20260213120001", Title = "Learning Rust", Content = "Rust is a systems programming language focused on safety." },
            new Note { Id = "20260213120002", Title = "Cooking Pasta", Content = "Boil water, add pasta, cook for 8 minutes." },
            new Note { Id = "20260213120003", Title = "Rust Ownership", Content = "Ownership is Rust's most unique feature and enables memory safety." });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());
        var results = await service.FullTextSearchAsync("rust");

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
            Assert.True(
                r.Title.Contains("rust", StringComparison.OrdinalIgnoreCase) ||
                r.Snippet.Contains("rust", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task FullTextSearchAsync_ReturnsEmptyForNoMatch()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note { Id = "n1", Title = "Rust", Content = "Systems programming" });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());
        var results = await service.FullTextSearchAsync("javascript");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FullTextSearchAsync_IsCaseInsensitive()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note { Id = "n1", Title = "Learning Rust", Content = "Rust programming" },
            new Note { Id = "n2", Title = "Rust Ownership", Content = "Ownership in Rust" });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());
        var results = await service.FullTextSearchAsync("RUST");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task FullTextSearchAsync_EmptyQueryReturnsEmpty()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FullTextSearchAsync("");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FullTextSearchAsync_WhitespaceQueryReturnsEmpty()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FullTextSearchAsync("   ");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FullTextSearchAsync_ResultsAreRanked()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note { Id = "n1", Title = "Rust Programming", Content = "Rust is great for systems programming with Rust." },
            new Note { Id = "n2", Title = "Cooking", Content = "Sometimes I think about rust on old pans." });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());
        var results = await service.FullTextSearchAsync("rust");

        Assert.True(results.Count >= 1);
        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Rank >= results[i].Rank,
                "Results should be ordered by rank descending");
        }
    }

    [Fact]
    public async Task FullTextSearchAsync_UsesStemming()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "n1", Title = "Programming Languages",
            Content = "Programming is about learning new languages and paradigms."
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        // "programs" should match "programming" via stemming
        var results = await service.FullTextSearchAsync("programs");

        Assert.Single(results);
    }

    // ── Semantic Search Tests ────────────────────────────────

    [Fact]
    public async Task SemanticSearchAsync_ReturnsNotesByCosineSimilarity()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "note1", Title = "Rust", Content = "Systems programming",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
            },
            new Note
            {
                Id = "note2", Title = "Cooking", Content = "Pasta recipe",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f, 0.0f },
            },
            new Note
            {
                Id = "note3", Title = "C++", Content = "Also systems programming",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f, 0.1f, 0.0f },
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.SemanticSearchAsync("systems programming");

        Assert.Equal(2, results.Count);
        Assert.Equal("note1", results[0].NoteId);
        Assert.Equal("note3", results[1].NoteId);
    }

    [Fact]
    public async Task SemanticSearchAsync_SkipsNotesWithoutEmbeddings()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "embedded", Title = "Has Embedding", Content = "Content",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
            },
            new Note
            {
                Id = "pending", Title = "No Embedding", Content = "Content",
                EmbedStatus = EmbedStatus.Pending,
                Embedding = null,
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.SemanticSearchAsync("query");

        Assert.Single(results);
        Assert.Equal("embedded", results[0].NoteId);
    }

    [Fact]
    public async Task SemanticSearchAsync_EmptyQueryReturnsEmpty()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.SemanticSearchAsync("");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SemanticSearchAsync_FiltersOutLowSimilarityResults()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "relevant", Title = "Relevant", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.95f, 0.05f, 0.0f },
            },
            new Note
            {
                Id = "irrelevant", Title = "Irrelevant", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f, 0.0f },
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.SemanticSearchAsync("query");

        Assert.Single(results);
        Assert.Equal("relevant", results[0].NoteId);
    }

    [Fact]
    public async Task SemanticSearchAsync_UsesConfigurableMinimumSimilarity()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "high", Title = "High", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.95f, 0.05f, 0.0f },
            },
            new Note
            {
                Id = "medium", Title = "Medium", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.6f, 0.4f, 0.0f },
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var strictWeights = new SearchWeights { MinimumSimilarity = 0.9 };
        var service = CreateService(context, generator, strictWeights);

        var results = await service.SemanticSearchAsync("query");

        Assert.Single(results);
        Assert.Equal("high", results[0].NoteId);
    }

    // ── Hybrid Search Tests ─────────────────────────────────

    [Fact]
    public async Task HybridSearchAsync_CombinesFullTextAndSemanticResults()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "both", Title = "Rust Programming", Content = "Rust is a systems language",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
            },
            new Note
            {
                Id = "textonly", Title = "Old Rust", Content = "Rust on old pans is hard to remove",
                EmbedStatus = EmbedStatus.Pending,
                Embedding = null,
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.HybridSearchAsync("rust");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.NoteId == "both");
        Assert.Contains(results, r => r.NoteId == "textonly");
    }

    [Fact]
    public async Task HybridSearchAsync_DeduplicatesResults()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "note1", Title = "Rust", Content = "Rust programming",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[] { 1.0f, 0.0f },
        });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.HybridSearchAsync("rust");

        Assert.Single(results);
        Assert.Equal("note1", results[0].NoteId);
    }

    [Fact]
    public async Task HybridSearchAsync_EmptyQueryReturnsEmpty()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.HybridSearchAsync("");

        Assert.Empty(results);
    }

    [Fact]
    public async Task HybridSearchAsync_FallsBackToFullTextWhenEmbeddingFails()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "note1", Title = "Rust Guide", Content = "A guide to rust programming",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[] { 1.0f, 0.0f },
        });
        await context.SaveChangesAsync();

        var generator = new ThrowingEmbeddingGenerator();
        var service = CreateService(context, generator);

        var results = await service.HybridSearchAsync("rust");

        Assert.Single(results);
        Assert.Equal("note1", results[0].NoteId);
    }

    [Fact]
    public async Task HybridSearchAsync_DoesNotReturnIrrelevantSemanticResults()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "mental", Title = "General mental models",
                Content = "The map is not the terrain",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f, 0.2f, 0.1f },
            },
            new Note
            {
                Id = "malleable", Title = "Essay On Malleable Software",
                Content = "Software should be malleable",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.2f, 0.9f, 0.1f },
            },
            new Note
            {
                Id = "reductionism", Title = "Reductionism In Nutrition",
                Content = "Nutrition science is reductionist",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.1f, 0.2f, 0.9f },
            });
        await context.SaveChangesAsync();

        // Query embedding close to "mental", far from others
        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.HybridSearchAsync("mental");

        Assert.Single(results);
        Assert.Equal("mental", results[0].NoteId);
    }

    [Fact]
    public async Task HybridSearchAsync_RanksAreNormalizedToZeroOne()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "n1", Title = "Rust", Content = "Rust programming",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "n2", Title = "Learning Rust", Content = "Rust is great for systems",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.8f, 0.2f },
            });
        await context.SaveChangesAsync();

        var generator = new FakeEmbeddingGenerator(new float[] { 1.0f, 0.0f });
        var service = CreateService(context, generator);

        var results = await service.HybridSearchAsync("rust");

        Assert.All(results, r =>
        {
            Assert.True(r.Rank >= 0.0, "Rank should be >= 0");
            Assert.True(r.Rank <= 1.0, "Rank should be <= 1");
        });
    }

    // ── FindRelated Tests ──────────────────────────────────

    [Fact]
    public async Task FindRelatedAsync_ReturnsTopSimilarNotes()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "source", Title = "Source", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "similar", Title = "Similar", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.95f, 0.05f },
            },
            new Note
            {
                Id = "different", Title = "Different", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FindRelatedAsync("source");

        Assert.Single(results);
        Assert.Equal("similar", results[0].NoteId);
    }

    [Fact]
    public async Task FindRelatedAsync_ExcludesSourceNote()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "source", Title = "Source", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
            },
            new Note
            {
                Id = "other", Title = "Other", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f, 0.1f },
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FindRelatedAsync("source");

        Assert.DoesNotContain(results, r => r.NoteId == "source");
    }

    [Fact]
    public async Task FindRelatedAsync_RespectsLimit()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "source", Title = "Source", Content = "C",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[] { 1.0f, 0.0f },
        });
        for (var i = 0; i < 10; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"note{i:D2}", Title = $"Note {i}", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f + i * 0.005f, 0.1f - i * 0.005f },
            });
        }
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FindRelatedAsync("source", limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task FindRelatedAsync_ReturnsEmptyWhenNoteNotFound()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FindRelatedAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindRelatedAsync_ReturnsEmptyWhenNoteHasNoEmbedding()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "source", Title = "Source", Content = "C",
            EmbedStatus = EmbedStatus.Pending,
            Embedding = null,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.FindRelatedAsync("source");

        Assert.Empty(results);
    }

    // ── Discover Tests ──────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_ReturnsSimilarNotesToRecentOnes()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            // Recent note (will be used as query base)
            new Note
            {
                Id = "recent1", Title = "Recent Rust", Content = "Rust programming",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
                UpdatedAt = DateTime.UtcNow,
            },
            // Similar to recent, should be discovered
            new Note
            {
                Id = "discover1", Title = "C++ Systems", Content = "Also systems programming",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.95f, 0.05f, 0.0f },
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
            },
            // Different, should not be discovered
            new Note
            {
                Id = "unrelated", Title = "Cooking", Content = "Pasta recipe",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 0.0f, 1.0f },
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.DiscoverAsync(recentCount: 1, limit: 5);

        Assert.Single(results);
        Assert.Equal("discover1", results[0].NoteId);
    }

    [Fact]
    public async Task DiscoverAsync_ExcludesRecentNotesFromResults()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "recent1", Title = "Recent", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f },
                UpdatedAt = DateTime.UtcNow,
            },
            new Note
            {
                Id = "older", Title = "Older Similar", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f, 0.1f },
                UpdatedAt = DateTime.UtcNow.AddDays(-5),
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.DiscoverAsync(recentCount: 1, limit: 5);

        Assert.DoesNotContain(results, r => r.NoteId == "recent1");
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsEmptyWhenNoNotesHaveEmbeddings()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.Add(new Note
        {
            Id = "pending1", Title = "Pending", Content = "C",
            EmbedStatus = EmbedStatus.Pending,
            Embedding = null,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.DiscoverAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task DiscoverAsync_RespectsLimit()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);

        // One recent note as query base
        context.Notes.Add(new Note
        {
            Id = "recent", Title = "Recent", Content = "C",
            EmbedStatus = EmbedStatus.Completed,
            Embedding = new float[] { 1.0f, 0.0f },
            UpdatedAt = DateTime.UtcNow,
        });

        // Many similar older notes
        for (var i = 0; i < 10; i++)
        {
            context.Notes.Add(new Note
            {
                Id = $"old{i:D2}", Title = $"Old {i}", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.9f + i * 0.005f, 0.1f - i * 0.005f },
                UpdatedAt = DateTime.UtcNow.AddDays(-10 - i),
            });
        }
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.DiscoverAsync(recentCount: 1, limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task DiscoverAsync_AveragesMultipleRecentNoteEmbeddings()
    {
        await using var context = CreateDbContext();
        await ClearNotesAsync(context);
        context.Notes.AddRange(
            new Note
            {
                Id = "recent1", Title = "Rust", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 1.0f, 0.0f, 0.0f },
                UpdatedAt = DateTime.UtcNow,
            },
            new Note
            {
                Id = "recent2", Title = "Go", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 1.0f, 0.0f },
                UpdatedAt = DateTime.UtcNow.AddSeconds(-1),
            },
            // Average of recent = [0.5, 0.5, 0.0]
            // This note is close to the average
            new Note
            {
                Id = "balanced", Title = "Balanced", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.5f, 0.5f, 0.0f },
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
            },
            // This note is far from the average
            new Note
            {
                Id = "unrelated", Title = "Cooking", Content = "C",
                EmbedStatus = EmbedStatus.Completed,
                Embedding = new float[] { 0.0f, 0.0f, 1.0f },
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateFakeGenerator());

        var results = await service.DiscoverAsync(recentCount: 2, limit: 5);

        // "balanced" should rank higher than "unrelated"
        Assert.Contains(results, r => r.NoteId == "balanced");
        if (results.Count > 1)
        {
            var balancedIdx = results.ToList().FindIndex(r => r.NoteId == "balanced");
            var unrelatedIdx = results.ToList().FindIndex(r => r.NoteId == "unrelated");
            if (unrelatedIdx >= 0)
                Assert.True(balancedIdx < unrelatedIdx);
        }
    }
}
