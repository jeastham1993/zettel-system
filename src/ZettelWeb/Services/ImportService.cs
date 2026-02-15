using Microsoft.EntityFrameworkCore;
using ZettelWeb.Background;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

public class ImportService : IImportService
{
    private readonly ZettelDbContext _db;
    private readonly IEmbeddingQueue _embeddingQueue;

    public ImportService(ZettelDbContext db, IEmbeddingQueue embeddingQueue)
    {
        _db = db;
        _embeddingQueue = embeddingQueue;
    }

    public async Task<ImportResult> ImportMarkdownAsync(IReadOnlyList<ImportFile> files)
    {
        var mdFiles = files.Where(f =>
            f.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToList();

        var skipped = files.Count - mdFiles.Count;
        var noteIds = new List<string>();
        var baseTime = DateTime.UtcNow;
        var usedIds = new HashSet<string>();

        for (var i = 0; i < mdFiles.Count; i++)
        {
            var file = mdFiles[i];

            Note note;
            if (NotionMarkdownParser.IsNotionFormat(file.Content))
            {
                var parsed = NotionMarkdownParser.Parse(file.Content);

                var title = parsed.Title
                    ?? Path.GetFileNameWithoutExtension(file.FileName);
                var id = parsed.Uid
                    ?? baseTime.AddSeconds(i).ToString("yyyyMMddHHmmssfff");

                // Skip duplicate UIDs (within batch or already in DB)
                if (usedIds.Contains(id) || await _db.Notes.AnyAsync(n => n.Id == id))
                {
                    skipped++;
                    continue;
                }

                var createdAt = parsed.Created ?? baseTime.AddSeconds(i);
                var updatedAt = parsed.LastEdited ?? createdAt;

                note = new Note
                {
                    Id = id,
                    Title = title,
                    Content = parsed.Body,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    EmbedStatus = EmbedStatus.Pending,
                };

                foreach (var tag in parsed.Tags)
                {
                    note.Tags.Add(new NoteTag { NoteId = id, Tag = tag });
                }

                usedIds.Add(id);
            }
            else
            {
                var title = Path.GetFileNameWithoutExtension(file.FileName);
                var id = baseTime.AddSeconds(i).ToString("yyyyMMddHHmmssfff");

                note = new Note
                {
                    Id = id,
                    Title = title,
                    Content = file.Content,
                    CreatedAt = baseTime.AddSeconds(i),
                    UpdatedAt = baseTime.AddSeconds(i),
                    EmbedStatus = EmbedStatus.Pending,
                };

                usedIds.Add(id);
            }

            _db.Notes.Add(note);
            noteIds.Add(note.Id);
        }

        await _db.SaveChangesAsync();

        foreach (var id in noteIds)
        {
            await _embeddingQueue.EnqueueAsync(id);
        }

        return new ImportResult(files.Count, noteIds.Count, skipped, noteIds);
    }
}
