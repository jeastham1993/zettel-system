namespace ZettelWeb.Models;

public class SearchResult
{
    public required string NoteId { get; set; }
    public required string Title { get; set; }
    public required string Snippet { get; set; }
    public double Rank { get; set; }
}

public class TitleSearchResult
{
    public required string NoteId { get; set; }
    public required string Title { get; set; }
}
