using System.Text.RegularExpressions;

namespace ZettelWeb.Services;

/// <summary>
/// Static helper for sanitising and extracting content from HTML.
/// Treats all input as untrusted — safe for use with externally-fetched content.
/// </summary>
public static partial class HtmlSanitiser
{
    private const int TruncateChars = 102_400; // 100KB regex processing limit

    public static string? ExtractTitle(string html)
    {
        var truncated = html.Length > TruncateChars ? html[..TruncateChars] : html;
        var match = TitleRegex().Match(truncated);
        if (!match.Success) return null;

        var title = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(title) ? null : System.Net.WebUtility.HtmlDecode(title);
    }

    public static string? ExtractDescription(string html)
    {
        var truncated = html.Length > TruncateChars ? html[..TruncateChars] : html;
        var match = MetaDescriptionRegex().Match(truncated);
        if (!match.Success)
            match = MetaDescriptionAltRegex().Match(truncated);
        if (!match.Success) return null;

        var desc = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(desc) ? null : System.Net.WebUtility.HtmlDecode(desc);
    }

    public static string? ExtractContentExcerpt(string html, int maxLength = 500)
    {
        var truncated = html.Length > TruncateChars ? html[..TruncateChars] : html;
        var bodyMatch = BodyRegex().Match(truncated);
        var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : truncated;

        body = ScriptStyleRegex().Replace(body, " ");
        body = HtmlTagRegex().Replace(body, " ");
        body = System.Net.WebUtility.HtmlDecode(body);
        body = WhitespaceRegex().Replace(body, " ").Trim();

        if (string.IsNullOrWhiteSpace(body)) return null;

        return body.Length > maxLength ? body[..maxLength] : body;
    }

    /// <summary>
    /// Strips all HTML tags and returns plain text. Used before passing
    /// external content to an LLM to reduce injection surface area.
    /// </summary>
    public static string StripToPlainText(string html)
    {
        var result = ScriptStyleRegex().Replace(html, " ");
        result = HtmlTagRegex().Replace(result, " ");
        result = System.Net.WebUtility.HtmlDecode(result);
        return WhitespaceRegex().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<meta\s+(?:[^>]*?\s+)?(?:name\s*=\s*[""']description[""']|property\s*=\s*[""']og:description[""'])\s+(?:[^>]*?\s+)?content\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex(@"<meta\s+(?:[^>]*?\s+)?content\s*=\s*[""']([^""']*)[""']\s+(?:[^>]*?\s+)?(?:name\s*=\s*[""']description[""']|property\s*=\s*[""']og:description[""'])", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescriptionAltRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.NonBacktracking)]
    internal static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>|<nav[^>]*>.*?</nav>|<header[^>]*>.*?</header>|<footer[^>]*>.*?</footer>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    internal static partial Regex ScriptStyleRegex();
}
