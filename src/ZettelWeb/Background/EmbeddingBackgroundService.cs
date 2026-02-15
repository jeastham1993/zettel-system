using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Background;

public partial class EmbeddingBackgroundService : BackgroundService
{
    private readonly IEmbeddingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmbeddingBackgroundService> _logger;
    private readonly int _maxInputCharacters;
    private readonly int _maxRetries;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiWhitespaceRegex();

    public EmbeddingBackgroundService(
        IEmbeddingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<EmbeddingBackgroundService> logger,
        IConfiguration configuration)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        // nomic-embed-text default num_ctx is 2048 tokens; 4000 chars ≈ 1000-1300 tokens, safe ceiling
        _maxInputCharacters = configuration.GetValue("Embedding:MaxInputCharacters", 4_000);
        _maxRetries = configuration.GetValue("Embedding:MaxRetries", 3);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverProcessingNotesAsync(stoppingToken);

        var channelTask = ProcessChannelAsync(stoppingToken);
        var pollTask = PollDatabaseAsync(stoppingToken);

        await Task.WhenAll(channelTask, pollTask);
    }

    private async Task ProcessChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (var noteId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessNoteAsync(noteId, stoppingToken);
        }
    }

    private async Task PollDatabaseAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);

                var noteIds = await GetPendingNoteIdsAsync(stoppingToken);

                foreach (var noteId in noteIds)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await ProcessNoteAsync(noteId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DB polling for embeddings");
            }
        }
    }

    public async Task RecoverProcessingNotesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        var stuckNotes = await db.Notes
            .Where(n => n.EmbedStatus == EmbedStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var note in stuckNotes)
        {
            note.EmbedStatus = EmbedStatus.Pending;
            _logger.LogWarning("Reset note {NoteId} from Processing to Pending on startup", note.Id);
        }

        if (stuckNotes.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingNoteIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();

        return await db.Notes
            .Where(n => n.EmbedStatus == EmbedStatus.Pending
                     || n.EmbedStatus == EmbedStatus.Failed
                     || n.EmbedStatus == EmbedStatus.Stale)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task ProcessNoteAsync(string noteId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZettelDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        var note = await db.Notes.FindAsync(new object[] { noteId }, cancellationToken);
        if (note is null)
        {
            _logger.LogWarning("Note {NoteId} not found for embedding", noteId);
            return;
        }

        if (note.EmbedStatus == EmbedStatus.Failed && note.EmbedRetryCount >= _maxRetries)
        {
            _logger.LogWarning("Note {NoteId} exceeded max embed retries ({MaxRetries}), skipping",
                noteId, _maxRetries);
            return;
        }

        // Set to processing
        note.EmbedStatus = EmbedStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            // Strip HTML tags from content to improve embedding quality
            var strippedContent = HtmlTagRegex().Replace(note.Content, " ");
            strippedContent = MultiWhitespaceRegex().Replace(strippedContent, " ").Trim();
            var text = $"{note.Title}\n\n{strippedContent}";
            if (text.Length > _maxInputCharacters)
            {
                _logger.LogWarning("Truncated note {NoteId} from {OriginalLength} to {MaxLength} characters for embedding",
                    noteId, text.Length, _maxInputCharacters);
                text = text[.._maxInputCharacters];
            }
            var vector = await generator.GenerateVectorAsync(text, cancellationToken: cancellationToken);
            var modelId = generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "unknown";

            note.Embedding = vector.ToArray();
            note.EmbedStatus = EmbedStatus.Completed;
            note.EmbeddingModel = modelId;
            note.EmbedError = null;
            note.EmbedUpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("Embedded note {NoteId} with model {Model}",
                noteId, modelId);
        }
        catch (Exception ex)
        {
            note.EmbedStatus = EmbedStatus.Failed;
            note.EmbedRetryCount++;
            note.EmbedError = ex.Message;
            note.EmbedUpdatedAt = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to embed note {NoteId}", noteId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
