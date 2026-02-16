using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public partial class NoteService : INoteService
{
    private readonly ZettelDbContext _db;
    private readonly IEmbeddingQueue _embeddingQueue;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly ILogger<NoteService>? _logger;

    public NoteService(ZettelDbContext db, IEmbeddingQueue embeddingQueue,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        ILogger<NoteService>? logger = null)
    {
        _db = db;
        _embeddingQueue = embeddingQueue;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    [GeneratedRegex(@"https?://[^\s<>""']+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[^\S\n]{2,}")]
    private static partial Regex MultiSpaceRegex();

    public Task<Note> CreateAsync(string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null)
    {
        return CreateCoreAsync(title, content, NoteStatus.Permanent, null, tags,
            noteType ?? NoteType.Regular, sourceAuthor, sourceTitle,
            sourceUrl, sourceYear, sourceType);
    }

    public Task<Note> CreateFleetingAsync(string content, string source,
        IEnumerable<string>? tags = null)
    {
        // Replace HTML tags with spaces, collapse horizontal whitespace (preserve newlines)
        var stripped = HtmlTagRegex().Replace(content, " ");
        stripped = MultiSpaceRegex().Replace(stripped, " ").Trim();
        var firstLine = stripped.Split('\n', 2)[0].Trim();
        var title = firstLine.Length > 60 ? firstLine[..60] + "..." : firstLine;
        if (string.IsNullOrWhiteSpace(title))
            title = "Fleeting note";

        return CreateCoreAsync(title, content, NoteStatus.Fleeting, source, tags,
            NoteType.Regular, null, null, null, null, null);
    }

    private async Task<Note> CreateCoreAsync(string title, string content,
        NoteStatus status, string? source, IEnumerable<string>? tags,
        NoteType noteType, string? sourceAuthor, string? sourceTitle,
        string? sourceUrl, int? sourceYear, string? sourceType)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("note.create");
        var now = DateTime.UtcNow;

        // 17-digit timestamp + 4-digit random suffix = 21 chars, virtually collision-free
        var id = $"{now:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 9999)}";

        var note = new Note
        {
            Id = id,
            Title = title,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
            Status = status,
            Source = source,
            NoteType = noteType,
            SourceAuthor = sourceAuthor,
            SourceTitle = sourceTitle,
            SourceUrl = sourceUrl,
            SourceYear = sourceYear,
            SourceType = sourceType,
            EmbedStatus = EmbedStatus.Pending,
        };

        if (tags is not null)
        {
            note.Tags = tags.Select(t => new NoteTag
            {
                NoteId = note.Id,
                Tag = t,
            }).ToList();
        }

        _db.Notes.Add(note);
        await _db.SaveChangesAsync();

        await _embeddingQueue.EnqueueAsync(note.Id);

        activity?.SetTag("note.id", note.Id);
        activity?.SetTag("note.status", status.ToString());
        activity?.SetTag("note.type", noteType.ToString());
        activity?.SetStatus(ActivityStatusCode.Ok);
        ZettelTelemetry.NotesCreated.Add(1,
            new KeyValuePair<string, object?>("note.status", status.ToString()),
            new KeyValuePair<string, object?>("note.type", noteType.ToString()));

        return note;
    }

    public async Task<Note?> GetByIdAsync(string id)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("note.get");
        activity?.SetTag("note.id", id);

        return await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == id);
    }

    public async Task<PagedResult<Note>> ListAsync(int skip = 0, int take = 50,
        NoteStatus? status = null, string? tag = null,
        NoteType? noteType = null)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("note.list");
        activity?.SetTag("note.skip", skip);
        activity?.SetTag("note.take", take);

        var query = _db.Notes.AsNoTracking().Include(n => n.Tags).AsQueryable();

        if (status.HasValue)
            query = query.Where(n => n.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(n => n.Tags.Any(t => t.Tag == tag));

        if (noteType.HasValue)
            query = query.Where(n => n.NoteType == noteType.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return new PagedResult<Note>(items, totalCount);
    }

    public async Task<Note?> PromoteAsync(string id, NoteType? targetType = null)
    {
        var note = await _db.Notes
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null)
            return null;

        if (note.Status == NoteStatus.Permanent)
            return note;

        note.Status = NoteStatus.Permanent;
        note.NoteType = targetType ?? NoteType.Regular;
        note.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return note;
    }

    public async Task<int> CountFleetingAsync()
    {
        return await _db.Notes.CountAsync(n => n.Status == NoteStatus.Fleeting);
    }

    public async Task<Note?> UpdateAsync(string id, string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("note.update");
        activity?.SetTag("note.id", id);

        var note = await _db.Notes
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null)
            return null;

        // Save version snapshot before overwriting
        var versionTags = note.Tags.Count > 0
            ? string.Join(",", note.Tags.Select(t => t.Tag))
            : null;

        _db.NoteVersions.Add(new NoteVersion
        {
            NoteId = note.Id,
            Title = note.Title,
            Content = note.Content,
            Tags = versionTags,
            SavedAt = DateTime.UtcNow,
        });

        var contentChanged = note.Content != content;

        note.Title = title;
        note.Content = content;
        note.UpdatedAt = DateTime.UtcNow;

        if (noteType.HasValue)
        {
            var oldType = note.NoteType;
            note.NoteType = noteType.Value;

            // Clear source fields when changing away from Source
            if (oldType == NoteType.Source && noteType.Value != NoteType.Source)
            {
                note.SourceAuthor = null;
                note.SourceTitle = null;
                note.SourceUrl = null;
                note.SourceYear = null;
                note.SourceType = null;
            }
        }

        // Set source metadata fields (only meaningful when NoteType=Source)
        if (note.NoteType == NoteType.Source)
        {
            if (sourceAuthor is not null) note.SourceAuthor = sourceAuthor;
            if (sourceTitle is not null) note.SourceTitle = sourceTitle;
            if (sourceUrl is not null) note.SourceUrl = sourceUrl;
            if (sourceYear.HasValue) note.SourceYear = sourceYear;
            if (sourceType is not null) note.SourceType = sourceType;
        }

        if (contentChanged)
        {
            note.EmbedStatus = EmbedStatus.Stale;
            if (UrlRegex().IsMatch(content))
                note.EnrichStatus = EnrichStatus.Pending;
        }

        if (tags is not null)
        {
            note.Tags.Clear();
            note.Tags.AddRange(tags.Select(t => new NoteTag
            {
                NoteId = note.Id,
                Tag = t,
            }));
        }

        await _db.SaveChangesAsync();

        if (contentChanged)
            await _embeddingQueue.EnqueueAsync(note.Id);

        return note;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("note.delete");
        activity?.SetTag("note.id", id);

        var note = await _db.Notes.FindAsync(id);

        if (note is null)
            return false;

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();

        activity?.SetStatus(ActivityStatusCode.Ok);
        ZettelTelemetry.NotesDeleted.Add(1);

        return true;
    }

    public async Task<int> ReEmbedAllAsync()
    {
        var filter = _db.Notes
            .Where(n => n.EmbedStatus != EmbedStatus.Pending
                     && n.EmbedStatus != EmbedStatus.Processing);

        int count;
        List<string> ids;

        try
        {
            // Bulk update in SQL — avoids loading full entities (including Embedding arrays)
            count = await filter.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.EmbedStatus, EmbedStatus.Pending)
                .SetProperty(n => n.EmbedRetryCount, 0)
                .SetProperty(n => n.EmbedError, (string?)null));

            if (count == 0)
                return 0;

            // Fetch only IDs for enqueuing
            ids = await _db.Notes
                .Where(n => n.EmbedStatus == EmbedStatus.Pending)
                .Select(n => n.Id)
                .ToListAsync();
        }
        catch (InvalidOperationException)
        {
            // InMemory provider does not support ExecuteUpdateAsync — fall back
            var notes = await filter.ToListAsync();
            if (notes.Count == 0)
                return 0;

            foreach (var note in notes)
            {
                note.EmbedStatus = EmbedStatus.Pending;
                note.EmbedRetryCount = 0;
                note.EmbedError = null;
            }

            await _db.SaveChangesAsync();
            count = notes.Count;
            ids = notes.Select(n => n.Id).ToList();
        }

        foreach (var id in ids)
        {
            await _embeddingQueue.EnqueueAsync(id);
        }

        return count;
    }

    public async Task<IReadOnlyList<string>> SearchTagsAsync(string prefix)
    {
        return await _db.NoteTags
            .AsNoTracking()
            .Where(t => t.Tag.StartsWith(prefix))
            .Select(t => t.Tag)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(
        string prefix, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return Array.Empty<TitleSearchResult>();

        var lowerPrefix = prefix.ToLowerInvariant();

        return await _db.Notes
            .AsNoTracking()
            .Where(n => n.Title.ToLower().Contains(lowerPrefix))
            .OrderBy(n => n.Title)
            .Take(limit)
            .Select(n => new TitleSearchResult { NoteId = n.Id, Title = n.Title })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BacklinkResult>> GetBacklinksAsync(string noteId)
    {
        var note = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId);
        if (note is null)
            return Array.Empty<BacklinkResult>();

        var wikiLink = $"[[{note.Title}]]";

        var backlinks = await _db.Notes
            .AsNoTracking()
            .Where(n => n.Id != noteId && n.Content.Contains(wikiLink))
            .Select(n => new BacklinkResult(n.Id, n.Title))
            .ToListAsync();

        return backlinks;
    }

    public async Task<Note?> MergeNoteAsync(string fleetingId, string targetId)
    {
        var fleeting = await _db.Notes
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == fleetingId);

        if (fleeting is null)
            return null;

        var target = await _db.Notes
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == targetId);

        if (target is null)
            return null;

        // Save version snapshot of target before merging
        var versionTags = target.Tags.Count > 0
            ? string.Join(",", target.Tags.Select(t => t.Tag))
            : null;

        _db.NoteVersions.Add(new NoteVersion
        {
            NoteId = target.Id,
            Title = target.Title,
            Content = target.Content,
            Tags = versionTags,
            SavedAt = DateTime.UtcNow,
        });

        // Append fleeting content to target
        target.Content = target.Content + "\n\n---\n\n" + fleeting.Content;
        target.UpdatedAt = DateTime.UtcNow;
        target.EmbedStatus = EmbedStatus.Stale;

        // Remove fleeting note
        _db.Notes.Remove(fleeting);

        await _db.SaveChangesAsync();

        // Re-queue target for embedding
        await _embeddingQueue.EnqueueAsync(target.Id);

        return target;
    }

    public async Task<IReadOnlyList<string>> GetSuggestedTagsAsync(
        string noteId, int count = 5)
    {
        var note = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note?.Embedding is null)
            return Array.Empty<string>();

        try
        {
            var sourceVector = new Vector(note.Embedding);
            var results = await _db.Database
                .SqlQuery<SuggestedTagRow>($"""
                    SELECT nt."Tag", COUNT(*) AS "Count"
                    FROM "Notes" n
                    JOIN "NoteTags" nt ON nt."NoteId" = n."Id"
                    WHERE n."Embedding" IS NOT NULL
                      AND n."Id" != {noteId}
                      AND 1.0 - (n."Embedding"::vector <=> {sourceVector}) >= 0.5
                    GROUP BY nt."Tag"
                    ORDER BY "Count" DESC
                    LIMIT {count + note.Tags.Count}
                    """)
                .ToListAsync();

            var existingTags = note.Tags.Select(t => t.Tag).ToHashSet();

            return results
                .Where(r => !existingTags.Contains(r.Tag))
                .Take(count)
                .Select(r => r.Tag)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            // InMemory provider doesn't support raw SQL with pgvector
            _logger?.LogWarning(ex, "Tag suggestion query failed for note {NoteId} - returning empty suggestions", noteId);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error in tag suggestion for note {NoteId}", noteId);
            return Array.Empty<string>();
        }
    }

    public async Task<DuplicateCheckResult> CheckDuplicateAsync(string content)
    {
        if (_embeddingGenerator is null)
            return new DuplicateCheckResult(false, null, null, 0);

        try
        {
            var vector = await _embeddingGenerator.GenerateVectorAsync(content);
            var queryParam = new Vector(vector.ToArray());

            var result = await _db.Database
                .SqlQuery<DuplicateRow>($"""
                    SELECT "Id" AS "NoteId",
                           "Title",
                           (1.0 - ("Embedding"::vector <=> {queryParam}))::float8 AS "Similarity"
                    FROM "Notes"
                    WHERE "Embedding" IS NOT NULL
                    ORDER BY "Embedding"::vector <=> {queryParam}
                    LIMIT 1
                    """)
                .ToListAsync();

            if (result.Count > 0 && result[0].Similarity > 0.92)
            {
                return new DuplicateCheckResult(
                    true,
                    result[0].NoteId,
                    result[0].Title,
                    result[0].Similarity);
            }

            return new DuplicateCheckResult(false, null, null,
                result.Count > 0 ? result[0].Similarity : 0);
        }
        catch (InvalidOperationException ex)
        {
            // InMemory provider doesn't support raw SQL with pgvector
            _logger?.LogWarning(ex, "Duplicate check query failed - returning not-duplicate");
            return new DuplicateCheckResult(false, null, null, 0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error in duplicate check");
            return new DuplicateCheckResult(false, null, null, 0);
        }
    }

    public async Task<IReadOnlyList<NoteVersion>> GetVersionsAsync(string noteId)
    {
        return await _db.NoteVersions
            .AsNoTracking()
            .Where(v => v.NoteId == noteId)
            .OrderByDescending(v => v.SavedAt)
            .ToListAsync();
    }

    public async Task<NoteVersion?> GetVersionAsync(string noteId, int versionId)
    {
        return await _db.NoteVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.NoteId == noteId && v.Id == versionId);
    }
}

// Internal row types for raw SQL results
internal class SuggestedTagRow
{
    public required string Tag { get; set; }
    public int Count { get; set; }
}

internal class DuplicateRow
{
    public required string NoteId { get; set; }
    public required string Title { get; set; }
    public double Similarity { get; set; }
}
