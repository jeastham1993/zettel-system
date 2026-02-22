namespace ZettelWeb.Models;

/// <summary>A tag associated with a note (composite key: NoteId + Tag).</summary>
public class NoteTag
{
    /// <summary>The ID of the parent note.</summary>
    public required string NoteId { get; set; }
    /// <summary>The tag value.</summary>
    public required string Tag { get; set; }
}
