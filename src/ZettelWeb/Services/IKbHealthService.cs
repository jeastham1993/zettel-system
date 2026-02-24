using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IKbHealthService
{
    Task<KbHealthOverview> GetOverviewAsync();
    Task<IReadOnlyList<ConnectionSuggestion>> GetConnectionSuggestionsAsync(string noteId, int limit = 5);
    Task<Note?> InsertWikilinkAsync(string orphanNoteId, string targetNoteId);
}
