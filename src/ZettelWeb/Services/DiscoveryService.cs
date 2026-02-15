using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class DiscoveryService : IDiscoveryService
{
    private readonly ZettelDbContext _db;

    public DiscoveryService(ZettelDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Note>> GetRandomForgottenAsync(int count = 3)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var candidates = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .Where(n => n.UpdatedAt < cutoff)
            .ToListAsync();

        if (candidates.Count == 0)
            return Array.Empty<Note>();

        // Shuffle and take requested count
        var rng = Random.Shared;
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        return candidates.Take(count).ToList();
    }

    public async Task<IReadOnlyList<Note>> GetOrphansAsync(int count = 3)
    {
        var orphans = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .Where(n => n.Tags.Count == 0 && !n.Content.Contains("[["))
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();

        return orphans;
    }

    public async Task<IReadOnlyList<Note>> GetThisDayInHistoryAsync()
    {
        var today = DateTime.UtcNow;
        var month = today.Month;
        var day = today.Day;

        var notes = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .Where(n => n.CreatedAt.Month == month && n.CreatedAt.Day == day)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return notes;
    }
}
