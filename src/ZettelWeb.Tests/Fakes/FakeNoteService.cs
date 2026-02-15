using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Fakes;

public class FakeNoteService : INoteService
{
    public List<Note> CreatedNotes { get; } = new();
    public string? LastFleetingContent { get; private set; }
    public string? LastFleetingSource { get; private set; }
    public List<string>? LastFleetingTags { get; private set; }

    public Task<Note> CreateAsync(string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null)
    {
        var note = new Note
        {
            Id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
            Title = title,
            Content = content
        };
        CreatedNotes.Add(note);
        return Task.FromResult(note);
    }

    public Task<Note> CreateFleetingAsync(string content, string source, IEnumerable<string>? tags = null)
    {
        LastFleetingContent = content;
        LastFleetingSource = source;
        LastFleetingTags = tags?.ToList();

        var note = new Note
        {
            Id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
            Title = content.Length > 50 ? content[..50] + "..." : content,
            Content = content,
            Status = NoteStatus.Fleeting,
            Source = source
        };
        if (tags is not null)
        {
            note.Tags = tags.Select(t => new NoteTag { NoteId = note.Id, Tag = t }).ToList();
        }
        CreatedNotes.Add(note);
        return Task.FromResult(note);
    }

    public Task<Note?> GetByIdAsync(string id) => Task.FromResult<Note?>(null);
    public Task<PagedResult<Note>> ListAsync(int skip = 0, int take = 50,
        NoteStatus? status = null, string? tag = null, NoteType? noteType = null)
        => Task.FromResult(new PagedResult<Note>(new List<Note>(), 0));
    public Task<Note?> PromoteAsync(string id, NoteType? targetType = null)
        => Task.FromResult<Note?>(null);
    public Task<int> CountFleetingAsync() => Task.FromResult(0);
    public Task<Note?> UpdateAsync(string id, string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null)
        => Task.FromResult<Note?>(null);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> SearchTagsAsync(string prefix)
        => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    public Task<int> ReEmbedAllAsync() => Task.FromResult(0);
    public Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(string prefix, int limit = 10)
        => Task.FromResult<IReadOnlyList<TitleSearchResult>>(new List<TitleSearchResult>());
    public Task<IReadOnlyList<BacklinkResult>> GetBacklinksAsync(string noteId)
        => Task.FromResult<IReadOnlyList<BacklinkResult>>(new List<BacklinkResult>());
    public Task<Note?> MergeNoteAsync(string fleetingId, string targetId)
        => Task.FromResult<Note?>(null);
    public Task<IReadOnlyList<string>> GetSuggestedTagsAsync(string noteId, int count = 5)
        => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    public Task<DuplicateCheckResult> CheckDuplicateAsync(string content)
        => Task.FromResult(new DuplicateCheckResult(false, null, null, 0));
    public Task<IReadOnlyList<NoteVersion>> GetVersionsAsync(string noteId)
        => Task.FromResult<IReadOnlyList<NoteVersion>>(new List<NoteVersion>());
    public Task<NoteVersion?> GetVersionAsync(string noteId, int versionId)
        => Task.FromResult<NoteVersion?>(null);
}
