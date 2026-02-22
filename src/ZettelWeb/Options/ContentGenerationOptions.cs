namespace ZettelWeb.Models;

/// <summary>Configuration options for the content generation LLM provider.</summary>
public class ContentGenerationOptions
{
    public const string SectionName = "ContentGeneration";

    /// <summary>LLM provider: "bedrock" or "openai".</summary>
    public string Provider { get; set; } = "bedrock";
    /// <summary>Model ID (e.g. "anthropic.claude-3-5-sonnet-20241022-v2:0" for Bedrock).</summary>
    public string Model { get; set; } = "anthropic.claude-3-5-sonnet-20241022-v2:0";
    /// <summary>AWS region for Bedrock provider.</summary>
    public string Region { get; set; } = "";
    /// <summary>API key for OpenAI provider.</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>Maximum tokens for LLM response.</summary>
    public int MaxTokens { get; set; } = 4096;
    /// <summary>Temperature for LLM sampling.</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Maximum cluster size for topic discovery.</summary>
    public int MaxClusterSize { get; set; } = 10;
    /// <summary>Minimum cluster size for topic discovery.</summary>
    public int MinClusterSize { get; set; } = 3;
    /// <summary>Maximum seed retry attempts before giving up.</summary>
    public int MaxSeedRetries { get; set; } = 3;
    /// <summary>Semantic similarity threshold for related notes.</summary>
    public double SemanticThreshold { get; set; } = 0.75;
}
