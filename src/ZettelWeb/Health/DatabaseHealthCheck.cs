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
        var totalNotes = await _db.Notes.CountAsync(cancellationToken);
        var embedded = await _db.Notes.CountAsync(
            n => n.EmbedStatus == EmbedStatus.Completed, cancellationToken);
        var pending = await _db.Notes.CountAsync(
            n => n.EmbedStatus == EmbedStatus.Pending, cancellationToken);
        var failed = await _db.Notes.CountAsync(
            n => n.EmbedStatus == EmbedStatus.Failed, cancellationToken);

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
