using System.Globalization;
using System.Text.RegularExpressions;

namespace ZettelWeb.Services;

public static partial class NotionMarkdownParser
{
    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+?[a-f0-9]{32}\.md\)")]
    private static partial Regex NotionLinkRegex();

    public static bool IsNotionFormat(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var lines = content.Split('\n');

        // Must start with H1
        var firstNonEmpty = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (firstNonEmpty is null || !firstNonEmpty.TrimStart().StartsWith("# "))
            return false;

        // Must have metadata keys in the first ~10 lines
        var headerLines = lines.Take(10);
        return headerLines.Any(l =>
        {
            var trimmed = l.Trim();
            return trimmed.StartsWith("UID:", StringComparison.Ordinal) ||
                   trimmed.StartsWith("Created:", StringComparison.Ordinal) ||
                   trimmed.StartsWith("Tags:", StringComparison.Ordinal);
        });
    }

    public static NotionParseResult Parse(string content)
    {
        var lines = content.Split('\n');

        string? title = null;
        string? uid = null;
        DateTime? created = null;
        DateTime? lastEdited = null;
        List<string> tags = [];
        int bodyStartIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // First non-empty line should be H1
            if (title is null && trimmed.StartsWith("# "))
            {
                title = trimmed[2..].Trim();
                continue;
            }

            // After title, parse metadata lines
            if (title is not null)
            {
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    // Blank line after metadata = body starts next
                    if (i > 1 && bodyStartIndex == 0)
                    {
                        bodyStartIndex = i + 1;
                        break;
                    }
                    continue;
                }

                if (trimmed.StartsWith("Tags:", StringComparison.Ordinal))
                {
                    var tagValue = trimmed["Tags:".Length..].Trim();
                    if (!string.IsNullOrEmpty(tagValue))
                    {
                        tags = tagValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();
                    }
                    continue;
                }

                if (trimmed.StartsWith("UID:", StringComparison.Ordinal))
                {
                    uid = trimmed["UID:".Length..].Trim();
                    continue;
                }

                if (trimmed.StartsWith("Created:", StringComparison.Ordinal))
                {
                    var dateStr = trimmed["Created:".Length..].Trim();
                    created = ParseNotionDate(dateStr);
                    continue;
                }

                if (trimmed.StartsWith("Last Edited:", StringComparison.Ordinal))
                {
                    var dateStr = trimmed["Last Edited:".Length..].Trim();
                    lastEdited = ParseNotionDate(dateStr);
                    continue;
                }

                // Non-metadata, non-blank line after title = no metadata block
                // Treat everything after title as body
                bodyStartIndex = i;
                break;
            }
        }

        // If we never found a blank line separator, body starts after last metadata
        if (bodyStartIndex == 0)
            bodyStartIndex = lines.Length;

        var bodyLines = lines.Skip(bodyStartIndex).ToArray();
        var body = string.Join('\n', bodyLines).TrimEnd('\r', '\n');

        // Convert Notion links to wiki-style links
        body = NotionLinkRegex().Replace(body, "[[${1}]]");

        return new NotionParseResult(title, uid, tags, created, lastEdited, body);
    }

    private static DateTime? ParseNotionDate(string dateStr)
    {
        if (DateTime.TryParseExact(dateStr, "d MMMM yyyy HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            return result;
        }

        return null;
    }
}

public record NotionParseResult(
    string? Title,
    string? Uid,
    IReadOnlyList<string> Tags,
    DateTime? Created,
    DateTime? LastEdited,
    string Body);
