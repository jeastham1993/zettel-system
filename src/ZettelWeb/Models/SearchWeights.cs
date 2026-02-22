namespace ZettelWeb.Models;

/// <summary>Configuration for hybrid search score weighting.</summary>
public class SearchWeights
{
    /// <summary>Weight applied to semantic (vector) search scores (0.0–1.0). Default: 0.7.</summary>
    public double SemanticWeight { get; set; } = 0.7;
    /// <summary>Weight applied to full-text search scores (0.0–1.0). Default: 0.3.</summary>
    public double FullTextWeight { get; set; } = 0.3;
    /// <summary>Minimum cosine similarity threshold for semantic results. Default: 0.5.</summary>
    public double MinimumSimilarity { get; set; } = 0.5;
    /// <summary>Minimum combined score for hybrid results. Default: 0.1.</summary>
    public double MinimumHybridScore { get; set; } = 0.1;
}
