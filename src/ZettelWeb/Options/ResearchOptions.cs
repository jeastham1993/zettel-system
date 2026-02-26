namespace ZettelWeb.Models;

public class ResearchOptions
{
    public const string SectionName = "Research";

    public int MaxFindingsPerRun { get; set; } = 5;
    public double DeduplicationThreshold { get; set; } = 0.85;
    public BraveSearchOptions BraveSearch { get; set; } = new();
}

public class BraveSearchOptions
{
    public string ApiKey { get; set; } = "";
}
