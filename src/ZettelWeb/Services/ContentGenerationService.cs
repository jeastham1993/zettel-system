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
        IReadOnlyList<string>? mediums = null,
        CancellationToken cancellationToken = default)
    {
        var generateBlog = mediums is null || mediums.Contains("blog", StringComparer.OrdinalIgnoreCase);
        var generateSocial = mediums is null || mediums.Contains("social", StringComparer.OrdinalIgnoreCase);

        using var activity = ZettelTelemetry.ActivitySource.StartActivity("content.generate");
        activity?.SetTag("content.seed_id", cluster.SeedNoteId);
        activity?.SetTag("content.cluster_size", cluster.Notes.Count);
        activity?.SetTag("content.generate_blog", generateBlog);
        activity?.SetTag("content.generate_social", generateSocial);

        _logger.LogInformation(
            "Generating content from cluster of {Count} notes (seed: {SeedId}, blog: {Blog}, social: {Social})",
            cluster.Notes.Count, cluster.SeedNoteId, generateBlog, generateSocial);

        var noteContext = BuildNoteContext(cluster.Notes);

        // Blog post generation (weekly cadence)
        string? blogTitle = null, blogDescription = null, blogBody = null, editorFeedback = null;
        List<string>? blogTags = null;
        if (generateBlog)
        {
            var blogVoice = await LoadVoiceAsync("blog", cancellationToken);
            (blogTitle, blogDescription, blogTags, blogBody) = await GenerateBlogPostAsync(
                noteContext, blogVoice, cancellationToken);
            // Run editor review (best-effort — failure is non-fatal)
            editorFeedback = await GenerateEditorFeedbackAsync(blogTitle, blogBody, cancellationToken);
        }

        // Social post generation (daily cadence)
        List<string> socialPosts = [];
        if (generateSocial)
        {
            var socialVoice = await LoadVoiceAsync("social", cancellationToken);
            socialPosts = await GenerateSocialPostsAsync(noteContext, socialVoice, cancellationToken);
        }

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

        if (generateBlog)
        {
            generation.Pieces.Add(new ContentPiece
            {
                Id = GenerateId(),
                GenerationId = generationId,
                Medium = "blog",
                Title = blogTitle,
                Description = blogDescription,
                GeneratedTags = blogTags ?? [],
                EditorFeedback = editorFeedback,
                Body = blogBody!,
                Status = ContentPieceStatus.Draft,
                Sequence = sequence++,
                CreatedAt = DateTime.UtcNow,
            });
        }

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
            "Generated content {GenerationId}: {BlogCount} blog + {SocialCount} social posts",
            generationId, generateBlog ? 1 : 0, socialPosts.Count);

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
            var (title, description, tags, body) = await GenerateBlogPostAsync(noteContext, voice, cancellationToken);
            var editorFeedback = await GenerateEditorFeedbackAsync(title, body, cancellationToken);
            newPieces =
            [
                new ContentPiece
                {
                    Id = GenerateId(),
                    GenerationId = generation.Id,
                    Medium = "blog",
                    Title = title,
                    Description = description,
                    GeneratedTags = tags,
                    EditorFeedback = editorFeedback,
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

    private async Task<(string title, string? description, List<string> tags, string body)> GenerateBlogPostAsync(
        string noteContext,
        (string styleNotes, List<string> examples) voice,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildBlogSystemPrompt(voice);
        var userPrompt = $"""
            Write a blog post based on the following notes from my knowledge base.
            Draw connections between the ideas and present them as a cohesive article.

            Format your response exactly as:
            TITLE: [Title here]
            DESCRIPTION: [One-sentence SEO description]
            TAGS: [tag1, tag2, tag3]

            [Body in markdown, starting on the line after the blank line]

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

    private async Task<string?> GenerateEditorFeedbackAsync(
        string title, string body, CancellationToken cancellationToken)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("content.editor_feedback");
        activity?.SetTag("content.piece_title", title);

        try
        {
            var userPrompt = $"""
                You are a careful editor reviewing a blog post draft. Provide concise, specific feedback on:

                1. **Spelling & Grammar** — flag any errors
                2. **Sloppy thinking** — vague claims, logical gaps, areas to expand or evidence
                3. **AI tells** — phrases that sound generic, hedging, or machine-generated

                Do NOT rewrite the post. Return only a bullet-point list of recommendations.
                If the post is clean in a category, say "✓ No issues found."

                --- BLOG POST ---
                Title: {title}

                {body}
                """;

            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, userPrompt)],
                new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    Temperature = 0.3f,
                },
                cancellationToken);

            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Editor feedback generation failed for blog post '{Title}' — continuing without it", title);
            return null;
        }
    }

    private async Task<List<string>> GenerateSocialPostsAsync(
        string noteContext,
        (string styleNotes, List<string> examples) voice,
        CancellationToken cancellationToken)
    {
        var systemPrompt = BuildSocialSystemPrompt(voice);
        var count = _options.SocialPostCount;
        var postWord = count == 1 ? "post" : "posts";
        var userPrompt = $"""
            Write {count} social media {postWord} based on the following notes from my knowledge base.
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
        sb.AppendLine("Output format (first 3 lines are required headers, then a blank line, then the body):");
        sb.AppendLine("TITLE: [Title here]");
        sb.AppendLine("DESCRIPTION: [One-sentence SEO description, max 160 chars]");
        sb.AppendLine("TAGS: [tag1, tag2, tag3]");
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

    private static (string title, string? description, List<string> tags, string body) ParseBlogResponse(string text)
    {
        var lines = text.Split('\n');
        string title = "Untitled";
        string? description = null;
        List<string> tags = [];
        var bodyStart = 0;

        // Try new structured format: TITLE:/DESCRIPTION:/TAGS: headers.
        // Skip any preamble lines before the first structured header so that
        // LLM output like "Sure! Here is your blog post:" does not abort parsing.
        var foundStructuredHeader = false;
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
            {
                title = line["TITLE:".Length..].Trim();
                foundStructuredHeader = true;
            }
            else if (line.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
            {
                description = line["DESCRIPTION:".Length..].Trim() ?? null;
                foundStructuredHeader = true;
            }
            else if (line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
            {
                tags = (line["TAGS:".Length..].Trim() ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                foundStructuredHeader = true;
            }
            else if (string.IsNullOrWhiteSpace(line) && foundStructuredHeader)
            {
                // Blank line after headers signals start of body
                bodyStart = i + 1;
                break;
            }
            else if (!string.IsNullOrWhiteSpace(line) && foundStructuredHeader)
            {
                // Non-blank line after we already parsed at least one header
                // means headers are done and body starts here (no blank separator)
                bodyStart = i;
                break;
            }
            // else: preamble line before any structured header — skip it
            i++;
        }

        // Fallback: legacy "# Title" format (used when no structured headers found)
        if (bodyStart == 0 && title == "Untitled")
        {
            for (i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("# ") && !line.StartsWith("## "))
                {
                    title = line[2..].Trim();
                    bodyStart = i + 1;
                    break;
                }
            }
        }

        var body = string.Join('\n', lines.Skip(bodyStart)).Trim();
        // Ensure no null/empty propagation: normalise empty strings to sensible defaults
        title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
        description = string.IsNullOrWhiteSpace(description) ? null : description;
        return (title, description, tags, body);
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
