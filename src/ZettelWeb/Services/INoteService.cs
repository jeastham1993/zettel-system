using ZettelWeb.Models;

namespace ZettelWeb.Services;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public interface INoteService
{
    Task<Note> CreateAsync(string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null);
    Task<Note> CreateFleetingAsync(string content, string source, IEnumerable<string>? tags = null);
    Task<Note?> GetByIdAsync(string id);
    Task<PagedResult<Note>> ListAsync(int skip = 0, int take = 50,
        NoteStatus? status = null, string? tag = null,
        NoteType? noteType = null);
    Task<Note?> PromoteAsync(string id, NoteType? targetType = null);
    Task<int> CountFleetingAsync();
    Task<Note?> UpdateAsync(string id, string title, string content,
        IEnumerable<string>? tags = null,
        NoteType? noteType = null,
        string? sourceAuthor = null, string? sourceTitle = null,
        string? sourceUrl = null, int? sourceYear = null,
        string? sourceType = null);
    Task<bool> DeleteAsync(string id);
    Task<IReadOnlyList<string>> SearchTagsAsync(string prefix);
    Task<int> ReEmbedAllAsync();
    Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(string prefix, int limit = 10);
    Task<IReadOnlyList<BacklinkResult>> GetBacklinksAsync(string noteId);
    Task<Note?> MergeNoteAsync(string fleetingId, string targetId);
    Task<IReadOnlyList<string>> GetSuggestedTagsAsync(string noteId, int count = 5);
    Task<DuplicateCheckResult> CheckDuplicateAsync(string content);
    Task<IReadOnlyList<NoteVersion>> GetVersionsAsync(string noteId);
    Task<NoteVersion?> GetVersionAsync(string noteId, int versionId);
}
