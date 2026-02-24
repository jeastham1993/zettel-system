using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class WikiLinkParserTests
{
    [Fact]
    public void ExtractLinkedTitles_ReturnsEmptyForContentWithNoLinks()
    {
        var titles = WikiLinkParser.ExtractLinkedTitles("Just some plain text.");
        Assert.Empty(titles);
    }

    [Fact]
    public void ExtractLinkedTitles_ReturnsSingleTitle()
    {
        var titles = WikiLinkParser.ExtractLinkedTitles("See [[Note One]] for details.").ToList();
        Assert.Single(titles);
        Assert.Equal("Note One", titles[0]);
    }

    [Fact]
    public void ExtractLinkedTitles_ReturnsMultipleTitles()
    {
        var titles = WikiLinkParser.ExtractLinkedTitles("See [[Alpha]] and [[Beta]].").ToList();
        Assert.Equal(2, titles.Count);
        Assert.Contains("Alpha", titles);
        Assert.Contains("Beta", titles);
    }

    [Fact]
    public void ExtractLinkedTitles_ReturnsEmptyForEmptyString()
    {
        Assert.Empty(WikiLinkParser.ExtractLinkedTitles(string.Empty));
    }

    [Fact]
    public void ExtractLinkedTitles_HandlesHtmlContent()
    {
        var html = "<p>See [[Rust Ownership]] for more on <strong>memory</strong>.</p>";
        var titles = WikiLinkParser.ExtractLinkedTitles(html).ToList();
        Assert.Single(titles);
        Assert.Equal("Rust Ownership", titles[0]);
    }
}
