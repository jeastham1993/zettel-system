namespace ZettelWeb.Models;

public class SearchWeights
{
    public double SemanticWeight { get; set; } = 0.7;
    public double FullTextWeight { get; set; } = 0.3;
    public double MinimumSimilarity { get; set; } = 0.5;
    public double MinimumHybridScore { get; set; } = 0.1;
}
