namespace ZettelWeb.Models;

public record ImportFile(string FileName, string Content);

public record ImportResult(int Total, int Imported, int Skipped, IReadOnlyList<string> NoteIds);
