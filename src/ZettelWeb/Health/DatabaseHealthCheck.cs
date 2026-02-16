using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ZettelDbContext _db;

    public DatabaseHealthCheck(ZettelDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var counts = await _db.Notes
            .GroupBy(n => n.EmbedStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalNotes = counts.Sum(c => c.Count);
        var embedded = counts.FirstOrDefault(c => c.Status == EmbedStatus.Completed)?.Count ?? 0;
        var pending = counts.FirstOrDefault(c => c.Status == EmbedStatus.Pending)?.Count ?? 0;
        var failed = counts.FirstOrDefault(c => c.Status == EmbedStatus.Failed)?.Count ?? 0;

        var data = new Dictionary<string, object>
        {
            ["total_notes"] = totalNotes,
            ["embedded"] = embedded,
            ["pending"] = pending,
            ["failed"] = failed,
        };

        return HealthCheckResult.Healthy(
            $"{totalNotes} notes ({embedded} embedded, {pending} pending, {failed} failed)",
            data);
    }
}
