namespace ZettelWeb.Models;

/// <summary>A markdown file to import as a note.</summary>
/// <param name="FileName">Original file name (used to derive the note title).</param>
/// <param name="Content">Raw markdown content of the file.</param>
public record ImportFile(string FileName, string Content);

/// <summary>Result of a bulk import operation.</summary>
/// <param name="Total">Total number of files submitted.</param>
/// <param name="Imported">Number of notes successfully imported.</param>
/// <param name="Skipped">Number of files skipped (e.g. empty or invalid).</param>
/// <param name="NoteIds">IDs of the newly created notes.</param>
public record ImportResult(int Total, int Imported, int Skipped, IReadOnlyList<string> NoteIds);
