using System.Text.RegularExpressions;

namespace ZettelWeb.Services;

/// <summary>Shared utility for extracting [[wiki-link]] titles from note content.</summary>
public static partial class WikiLinkParser
{
    /// <summary>Returns all titles referenced by [[wiki-links]] in the given HTML content.</summary>
    public static IEnumerable<string> ExtractLinkedTitles(string content)
        => WikiLinkRegex().Matches(content).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\[\[([^\]]+)\]\]")]
    public static partial Regex WikiLinkRegex();
}
