using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ZettelWeb.Data;

namespace ZettelWeb.Services;

public partial class ExportService : IExportService
{
    private readonly ZettelDbContext _db;

    [GeneratedRegex(@"[/\\:*?""<>|]")]
    private static partial Regex UnsafeFileNameCharsRegex();

    public ExportService(ZettelDbContext db)
    {
        _db = db;
    }

    public async Task<byte[]> ExportAllAsZipAsync()
    {
        var notes = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var note in notes)
            {
                var fileName = $"{SanitizeFileName(note.Title)}.md";
                var entry = archive.CreateEntry(fileName);

                await using var entryStream = entry.Open();
                var content = BuildMarkdown(note);
                var bytes = Encoding.UTF8.GetBytes(content);
                await entryStream.WriteAsync(bytes);
            }
        }

        return memoryStream.ToArray();
    }

    private static string SanitizeFileName(string title)
    {
        // Remove path traversal sequences (loop to handle nested like "..../")
        var sanitized = title;
        while (sanitized.Contains(".."))
            sanitized = sanitized.Replace("..", "");
        // Remove unsafe filesystem characters
        sanitized = UnsafeFileNameCharsRegex().Replace(sanitized, "_");
        // Trim leading/trailing whitespace and dots
        sanitized = sanitized.Trim().Trim('.');
        // Fallback if the name is empty after sanitization
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized;
    }

    private static string BuildMarkdown(Models.Note note)
    {
        var sb = new StringBuilder();

        // YAML front matter
        sb.AppendLine("---");
        sb.AppendLine($"id: {note.Id}");
        sb.AppendLine($"created: {note.CreatedAt:O}");
        sb.AppendLine($"updated: {note.UpdatedAt:O}");

        if (note.Tags.Count > 0)
        {
            var tagList = string.Join(", ", note.Tags.Select(t => t.Tag));
            sb.AppendLine($"tags: [{tagList}]");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(note.Content);

        return sb.ToString();
    }
}
