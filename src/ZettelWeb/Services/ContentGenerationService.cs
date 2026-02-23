using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class ContentGenerationService : IContentGenerationService
{
    private readonly ZettelDbContext _db;
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ContentGenerationOptions _options;
    private readonly ILogger<ContentGenerationService> _logger;

    public ContentGenerationService(
        ZettelDbContext db,
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<ContentGenerationOptions> options,
        ILogger<ContentGenerationService> logger)
    {
        _db = db;
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ContentGeneration> GenerateContentAsync(
        TopicCluster cluster,
        CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("content.generate");
        activity?.SetTag("content.seed_id", cluster.SeedNoteId);
        activity?.SetTag("content.cluster_size", cluster.Notes.Count);

        _logger.LogInformation(
            "Generating content from cluster of {Count} notes (seed: {SeedId})",
            cluster.Notes.Count, cluster.SeedNoteId);

        // Load voice configuration
        var blogVoice = await LoadVoiceAsync("blog", cancellationToken);
        var socialVoice = await LoadVoiceAsync("social", cancellationToken);

        var noteContext = BuildNoteContext(cluster.Notes);

        // Generate blog post
        var (blogTitle, blogBody) = await GenerateBlogPostAsync(
            noteContext, blogVoice, cancellationToken);

        // Generate social posts
        var socialPosts = await GenerateSocialPostsAsync(
            noteContext, socialVoice, cancellationToken);

        // Generate topic embedding for overlap detection
        float[]? topicEmbedding = null;
        try
        {
            var vector = await _embeddingGenerator.GenerateVectorAsync(
                cluster.TopicSummary, cancellationToken: cancellationToken);
            topicEmbedding = vector.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate topic embedding, continuing without it");
        }

        // Persist to database
        var generationId = GenerateId();
        var generation = new ContentGeneration
        {
            Id = generationId,
            SeedNoteId = cluster.SeedNoteId,
            ClusterNoteIds = cluster.Notes.Select(n => n.Id).ToList(),
            TopicSummary = cluster.TopicSummary,
            TopicEmbedding = topicEmbedding,
            Status = GenerationStatus.Generated,
            GeneratedAt = DateTime.UtcNow,
        };

        var sequence = 1;

        generation.Pieces.Add(new ContentPiece
        {
            Id = GenerateId(),
            GenerationId = generationId,
            Medium = "blog",
            Title = blogTitle,
            Body = blogBody,
            Status = ContentPieceStatus.Draft,
            Sequence = sequence++,
            CreatedAt = DateTime.UtcNow,
        });

        foreach (var post in socialPosts)
        {
            generation.Pieces.Add(new ContentPiece
            {
                Id = GenerateId(),
                GenerationId = generationId,
                Medium = "social",
                Title = null,
                Body = post,
                Status = ContentPieceStatus.Draft,
                Sequence = sequence++,
                CreatedAt = DateTime.UtcNow,
            });
        }

        // Mark seed note as used (guard: skip if already tracked, e.g. during regeneration)
        if (!await _db.UsedSeedNotes.AnyAsync(u => u.NoteId == cluster.SeedNoteId, cancellationToken))
        {
            _db.UsedSeedNotes.Add(new UsedSeedNote
            {
                NoteId = cluster.SeedNoteId,
                UsedAt = DateTime.UtcNow,
            });
        }

        _db.ContentGenerations.Add(generation);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Generated content {GenerationId}: 1 blog + {SocialCount} social posts",
            generationId, socialPosts.Count);

        activity?.SetTag("content.generation_id", generationId);
        activity?.SetTag("content.piece_count", generation.Pieces.Count);
        ZettelTelemetry.ContentGenerated.Add(1);

        return generation;
    }

    public async Task<List<ContentPiece>> RegenerateMediumAsync(
        ContentGeneration generation,
        IReadOnlyList<Note> notes,
        string medium,
        CancellationToken cancellationToken = default)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("content.regenerate_medium");
        activity?.SetTag("content.generation_id", generation.Id);
        activity?.SetTag("content.medium", medium);

        _logger.LogInformation(
            "Regenerating {Medium} pieces for generation {GenerationId}",
            medium, generation.Id);

        var voice = await LoadVoiceAsync(medium, cancellationToken);
        var noteContext = BuildNoteContext(notes);

        List<ContentPiece> newPieces;

        if (medium == "blog")
        {
            var (title, body) = await GenerateBlogPostAsync(noteContext, voice, cancellationToken);
            newPieces =
            [
                new ContentPiece
                {
                    Id = GenerateId(),
                    GenerationId = generation.Id,
                    Medium = "blog",
                    Title = title,
                    Body = body,
                    Status = ContentPieceStatus.Draft,
                    Sequence = 1,
                    CreatedAt = DateTime.UtcNow,
                }
            ];
        }
        else
        {
            // Place social posts after whatever non-social pieces remain
            var nextSequence = await _db.ContentPieces
                .Where(p => p.GenerationId == generation.Id && p.Medium != medium)
                .MaxAsync(p => (int?)p.Sequence, cancellationToken) ?? 0;
            nextSequence++;

            var posts = await GenerateSocialPostsAsync(noteContext, voice, cancellationToken);
            newPieces = posts.Select(post => new ContentPiece
            {
                Id = GenerateId(),
                GenerationId = generation.Id,
                Medium = "social",
                Body = post,
                Status = ContentPieceStatus.Draft,
                Sequence = nextSequence++,
                CreatedAt = DateTime.UtcNow,
            }).ToList();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var draftPieces = await _db.ContentPieces
            .Where(p => p.GenerationId == generation.Id
                     && p.Medium == medium
                     && p.Status == ContentPieceStatus.Draft)
            .ToListAsync(cancellationToken);

        _db.ContentPieces.RemoveRange(draftPieces);
        _db.ContentPieces.AddRange(newPieces);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Regenerated {Count} {Medium} pieces for generation {GenerationId}",
            newPieces.Count, medium, generation.Id);

        activity?.SetTag("content.piece_count", newPieces.Count);

        return newPieces;
    }

    private async Task<(string styleNotes, List<string> examples)> LoadVoiceAsync(
        string medium, CancellationToken cancellationToken)
    {
        var configs = await _db.VoiceConfigs
            .AsNoTracking()
            .Where(c => c.Medium == medium || c.Medium == "all")
            .ToListAsync(cancellationToken);

        var styleNotes = string.Join("\n",
            configs.Where(c => !string.IsNullOrWhiteSpace(c.StyleNotes))
                   .Select(c => c.StyleNotes!));

        var examples = await _db.VoiceExamples
            .AsNoTracking()
            .Where(e => e.Medium == medium || e.Medium == "all")
            .Select(e => e.Content)
            .ToListAsync(cancellationToken);

        return (styleNotes, examples);
    }

    private static string BuildNoteContext(IReadOnlyList<Note> notes)
    {
        var sb = new StringBuilder();
        foreach (var note in notes)
        {
            sb.AppendLine($"## {note.Title}");
            sb.AppendLine();
            sb.AppendLine(note.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<(string title, string body)> GenerateBlogPostAsync(
        string noteContext,
        (string styleNotes, List<string> examples) voice,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildBlogSystemPrompt(voice);
        var userPrompt = $"""
            Write a blog post based on the following notes from my knowledge base.
            Draw connections between the ideas and present them as a cohesive article.

            Format your response as:
            # [Title]

            [Body in markdown]

            --- NOTES ---
            {noteContext}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            new ChatOptions
            {
                MaxOutputTokens = _options.MaxTokens,
                Temperature = _options.Temperature,
            },
            cancellationToken);

        var text = response.Text ?? "";
        return ParseBlogResponse(text);
    }

    private async Task<List<string>> GenerateSocialPostsAsync(
        string noteContext,
        (string styleNotes, List<string> examples) voice,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildSocialSystemPrompt(voice);
        var userPrompt = $"""
            Write 3-5 social media posts based on the following notes from my knowledge base.
            Each post should stand alone and offer a distinct angle: an insight, a question,
            a hot take, or a key takeaway.

            Separate each post with ---

            --- NOTES ---
            {noteContext}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            new ChatOptions
            {
                MaxOutputTokens = _options.MaxTokens,
                Temperature = _options.Temperature,
            },
            cancellationToken);

        var text = response.Text ?? "";
        return ParseSocialResponse(text);
    }

    private static string BuildBlogSystemPrompt(
        (string styleNotes, List<string> examples) voice)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a skilled writer helping to create blog content.");
        sb.AppendLine("Write in first person as if you are the author.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(voice.styleNotes))
        {
            sb.AppendLine("## Voice & Style");
            sb.AppendLine(voice.styleNotes);
            sb.AppendLine();
        }

        if (voice.examples.Count > 0)
        {
            sb.AppendLine("## Writing Examples (match this style)");
            foreach (var example in voice.examples.Take(3))
            {
                sb.AppendLine(example);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Format");
        sb.AppendLine("Choose the appropriate format based on the depth of material:");
        sb.AppendLine("- Full post (800-1,500 words) when the material is rich");
        sb.AppendLine("- Focused insight (300-600 words) when the material is thinner");
        sb.AppendLine();
        sb.AppendLine("Output format:");
        sb.AppendLine("# [Title]");
        sb.AppendLine();
        sb.AppendLine("[Body in markdown]");

        return sb.ToString();
    }

    private static string BuildSocialSystemPrompt(
        (string styleNotes, List<string> examples) voice)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a skilled writer helping to create social media content.");
        sb.AppendLine("Write in first person as if you are the author.");
        sb.AppendLine("Each post should be concise, engaging, and stand alone.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(voice.styleNotes))
        {
            sb.AppendLine("## Voice & Style");
            sb.AppendLine(voice.styleNotes);
            sb.AppendLine();
        }

        if (voice.examples.Count > 0)
        {
            sb.AppendLine("## Writing Examples (match this style)");
            foreach (var example in voice.examples.Take(3))
            {
                sb.AppendLine(example);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Format");
        sb.AppendLine("Write 3-5 varied social media posts. Separate each with ---");
        sb.AppendLine("Vary the angles: insights, questions, hot takes, key takeaways.");

        return sb.ToString();
    }

    private static (string title, string body) ParseBlogResponse(string text)
    {
        var lines = text.Split('\n');
        string title = "Untitled";
        var bodyStart = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("# ") && !line.StartsWith("## "))
            {
                title = line[2..].Trim();
                bodyStart = i + 1;
                break;
            }
        }

        var body = string.Join('\n', lines.Skip(bodyStart)).Trim();
        return (title, body);
    }

    private static List<string> ParseSocialResponse(string text)
    {
        var posts = text
            .Split("---", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return posts.Count > 0 ? posts : [text.Trim()];
    }

    private static string GenerateId()
    {
        var now = DateTime.UtcNow;
        return $"{now:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 9999)}";
    }
}
