using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class NotionMarkdownParserTests
{
    private const string FullNotionContent = """
        # My Notion Note

        Tags: zettelkasten, productivity
        UID: 202601011200
        Created: 15 January 2026 10:30
        Last Edited: 20 January 2026 14:45

        This is the body content.

        It has multiple paragraphs.
        """;

    // --- IsNotionFormat detection tests ---

    [Fact]
    public void IsNotionFormat_ValidNotionContent_ReturnsTrue()
    {
        Assert.True(NotionMarkdownParser.IsNotionFormat(FullNotionContent));
    }

    [Fact]
    public void IsNotionFormat_H1WithUidOnly_ReturnsTrue()
    {
        var content = "# Title\n\nUID: 202601011200\n\nBody text";
        Assert.True(NotionMarkdownParser.IsNotionFormat(content));
    }

    [Fact]
    public void IsNotionFormat_H1WithCreatedOnly_ReturnsTrue()
    {
        var content = "# Title\n\nCreated: 1 January 2026 09:00\n\nBody";
        Assert.True(NotionMarkdownParser.IsNotionFormat(content));
    }

    [Fact]
    public void IsNotionFormat_H1WithTagsOnly_ReturnsTrue()
    {
        var content = "# Title\n\nTags: one, two\n\nBody";
        Assert.True(NotionMarkdownParser.IsNotionFormat(content));
    }

    [Fact]
    public void IsNotionFormat_PlainMarkdown_ReturnsFalse()
    {
        var content = "Just some plain markdown content\nwith no H1 heading";
        Assert.False(NotionMarkdownParser.IsNotionFormat(content));
    }

    [Fact]
    public void IsNotionFormat_H1WithoutMetadata_ReturnsFalse()
    {
        var content = "# My Title\n\nJust body, no metadata keys at all.";
        Assert.False(NotionMarkdownParser.IsNotionFormat(content));
    }

    [Fact]
    public void IsNotionFormat_EmptyContent_ReturnsFalse()
    {
        Assert.False(NotionMarkdownParser.IsNotionFormat(""));
    }

    [Fact]
    public void IsNotionFormat_NullContent_ReturnsFalse()
    {
        Assert.False(NotionMarkdownParser.IsNotionFormat(null!));
    }

    [Fact]
    public void IsNotionFormat_H2InsteadOfH1_ReturnsFalse()
    {
        var content = "## Title\n\nUID: 202601011200\n\nBody";
        Assert.False(NotionMarkdownParser.IsNotionFormat(content));
    }

    // --- Parse: title extraction ---

    [Fact]
    public void Parse_ExtractsTitle()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.Equal("My Notion Note", result.Title);
    }

    [Fact]
    public void Parse_TitleWithLeadingTrailingWhitespace_IsTrimmed()
    {
        var content = "#   Spaced Title   \n\nUID: 123456789012\n\nBody";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Equal("Spaced Title", result.Title);
    }

    // --- Parse: UID extraction ---

    [Fact]
    public void Parse_ExtractsUid()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.Equal("202601011200", result.Uid);
    }

    [Fact]
    public void Parse_MissingUid_ReturnsNull()
    {
        var content = "# Title\n\nTags: one\n\nBody content";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Null(result.Uid);
    }

    // --- Parse: tags extraction ---

    [Fact]
    public void Parse_ExtractsTags()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.Equal(new List<string> { "zettelkasten", "productivity" }, result.Tags);
    }

    [Fact]
    public void Parse_SingleTag()
    {
        var content = "# Title\n\nTags: single\nUID: 123456789012\n\nBody";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Single(result.Tags);
        Assert.Equal("single", result.Tags[0]);
    }

    [Fact]
    public void Parse_NoTags_ReturnsEmptyList()
    {
        var content = "# Title\n\nUID: 123456789012\n\nBody content";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Empty(result.Tags);
    }

    // --- Parse: date extraction ---

    [Fact]
    public void Parse_ExtractsCreatedDate()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.NotNull(result.Created);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc), result.Created);
    }

    [Fact]
    public void Parse_ExtractsLastEditedDate()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.NotNull(result.LastEdited);
        Assert.Equal(new DateTime(2026, 1, 20, 14, 45, 0, DateTimeKind.Utc), result.LastEdited);
    }

    [Fact]
    public void Parse_MissingDates_ReturnsNull()
    {
        var content = "# Title\n\nUID: 123456789012\n\nBody";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Null(result.Created);
        Assert.Null(result.LastEdited);
    }

    [Fact]
    public void Parse_InvalidDateFormat_ReturnsNull()
    {
        var content = "# Title\n\nCreated: 2026-01-15\n\nBody";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Null(result.Created);
    }

    // --- Parse: body content ---

    [Fact]
    public void Parse_ExtractsBodyContent()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.Contains("This is the body content.", result.Body);
        Assert.Contains("It has multiple paragraphs.", result.Body);
    }

    [Fact]
    public void Parse_BodyExcludesMetadata()
    {
        var result = NotionMarkdownParser.Parse(FullNotionContent);
        Assert.DoesNotContain("Tags:", result.Body);
        Assert.DoesNotContain("UID:", result.Body);
        Assert.DoesNotContain("Created:", result.Body);
        Assert.DoesNotContain("# My Notion Note", result.Body);
    }

    // --- Parse: Notion link conversion ---

    [Fact]
    public void Parse_ConvertsNotionLinks()
    {
        var content = """
            # Title

            UID: 123456789012

            See [Another Note](Another%20Note%20a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4.md) for details.
            """;
        var result = NotionMarkdownParser.Parse(content);
        Assert.Contains("[[Another Note]]", result.Body);
        Assert.DoesNotContain(".md", result.Body);
    }

    [Fact]
    public void Parse_ConvertsMultipleNotionLinks()
    {
        var content = """
            # Title

            UID: 123456789012

            Link to [Note A](Note%20A%20aaaabbbbccccddddeeeeffffaaaabbbb.md) and [Note B](Note%20B%2011112222333344445555666677778888.md).
            """;
        var result = NotionMarkdownParser.Parse(content);
        Assert.Contains("[[Note A]]", result.Body);
        Assert.Contains("[[Note B]]", result.Body);
    }

    [Fact]
    public void Parse_PreservesRegularMarkdownLinks()
    {
        var content = """
            # Title

            UID: 123456789012

            Visit [Google](https://google.com) and [Another Note](Another%20aaaabbbbccccddddeeeeffffaaaabbbb.md).
            """;
        var result = NotionMarkdownParser.Parse(content);
        Assert.Contains("[Google](https://google.com)", result.Body);
        Assert.Contains("[[Another Note]]", result.Body);
    }

    // --- Parse: CRLF handling ---

    [Fact]
    public void Parse_HandlesCRLF()
    {
        var content = "# Title\r\n\r\nUID: 123456789012\r\nTags: one, two\r\n\r\nBody with CRLF.\r\nSecond line.";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Equal("Title", result.Title);
        Assert.Equal("123456789012", result.Uid);
        Assert.Equal(2, result.Tags.Count);
        Assert.Contains("Body with CRLF.", result.Body);
    }

    // --- Parse: whitespace in tags ---

    [Fact]
    public void Parse_TagsWithExtraWhitespace_AreTrimmed()
    {
        var content = "# Title\n\nTags:   one ,  two  ,  three  \n\nBody";
        var result = NotionMarkdownParser.Parse(content);
        Assert.Equal(new List<string> { "one", "two", "three" }, result.Tags);
    }
}
