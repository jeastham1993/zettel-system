namespace ZettelWeb.Services.Publishing;

public class PublishingOptions
{
    public const string SectionName = "Publishing";
    public GitHubOptions GitHub { get; init; } = new();
    public PublerOptions Publer { get; init; } = new();
}

public class GitHubOptions
{
    public string? Token { get; init; }
    public string Owner { get; init; } = "";
    public string Repo { get; init; } = "";
    public string Branch { get; init; } = "main";
    public string ContentPath { get; init; } = "src/content/blog";
    public string Author { get; init; } = "James Eastham";

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Token) &&
        !string.IsNullOrEmpty(Owner) &&
        !string.IsNullOrEmpty(Repo);
}

public class PublerOptions
{
    public string? ApiKey { get; init; }
    public string? WorkspaceId { get; init; }
    public List<PublerAccount> Accounts { get; init; } = [];

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) && Accounts.Count > 0;
}

public class PublerAccount
{
    public string Id { get; init; } = "";
    /// <summary>Publer network key, e.g. "linkedin", "twitter", "bluesky".</summary>
    public string Platform { get; init; } = "linkedin";
}
