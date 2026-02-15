using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IDiscoveryService
{
    Task<IReadOnlyList<Note>> GetRandomForgottenAsync(int count = 3);
    Task<IReadOnlyList<Note>> GetOrphansAsync(int count = 3);
    Task<IReadOnlyList<Note>> GetThisDayInHistoryAsync();
}
