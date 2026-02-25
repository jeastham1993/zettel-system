using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IKbHealthService
{
    Task<KbHealthOverview> GetOverviewAsync();
    Task<IReadOnlyList<ConnectionSuggestion>> GetConnectionSuggestionsAsync(string noteId, int limit = 5);
    Task<Note?> InsertWikilinkAsync(string orphanNoteId, string targetNoteId);
    Task<IReadOnlyList<UnembeddedNote>> GetNotesWithoutEmbeddingsAsync();
    Task<int> RequeueEmbeddingAsync(string noteId);
    Task<IReadOnlyList<LargeNote>> GetLargeNotesAsync();
    Task<SummarizeNoteResponse?> SummarizeNoteAsync(string noteId, CancellationToken cancellationToken = default);
    Task<SplitSuggestion?> GetSplitSuggestionsAsync(string noteId, CancellationToken cancellationToken = default);
    Task<ApplySplitResponse?> ApplySplitAsync(string noteId, IReadOnlyList<SuggestedNote> notes, CancellationToken cancellationToken = default);
}
