namespace ZettelWeb.Models;

/// <summary>A search result with relevance ranking.</summary>
public class SearchResult
{
    /// <summary>The ID of the matched note.</summary>
    public required string NoteId { get; set; }
    /// <summary>The title of the matched note.</summary>
    public required string Title { get; set; }
    /// <summary>A text snippet highlighting the match.</summary>
    public required string Snippet { get; set; }
    /// <summary>Relevance score (higher is more relevant).</summary>
    public double Rank { get; set; }
}

/// <summary>A lightweight title-only search result for autocomplete.</summary>
public class TitleSearchResult
{
    /// <summary>The ID of the matched note.</summary>
    public required string NoteId { get; set; }
    /// <summary>The title of the matched note.</summary>
    public required string Title { get; set; }
}
