namespace ZettelWeb.Services.Publishing;

public class PublishingOptions
{
    public const string SectionName = "Publishing";
    public GitHubOptions GitHub { get; init; } = new();
    public PublerOptions Publer { get; init; } = new();
}

public class GitHubOptions
{
    public string? Token { get; set; }
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string ContentPath { get; set; } = "src/content/blog";
    public string Author { get; set; } = "James Eastham";

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Token) &&
        !string.IsNullOrEmpty(Owner) &&
        !string.IsNullOrEmpty(Repo);
}

public class PublerOptions
{
    public string? ApiKey { get; set; }
    public string? WorkspaceId { get; set; }
    public List<PublerAccount> Accounts { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) && Accounts.Count > 0;
}

public class PublerAccount
{
    public string Id { get; set; } = "";
    /// <summary>Publer network key, e.g. "linkedin", "twitter", "bluesky".</summary>
    public string Platform { get; set; } = "linkedin";
}
