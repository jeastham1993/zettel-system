namespace ZettelWeb.Models;

/// <summary>Knowledge graph containing nodes (notes) and edges (relationships).</summary>
public class GraphData
{
    /// <summary>All notes as graph nodes.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; set; } = Array.Empty<GraphNode>();
    /// <summary>Relationships between notes (wikilinks and semantic similarity).</summary>
    public IReadOnlyList<GraphEdge> Edges { get; set; } = Array.Empty<GraphEdge>();
}

/// <summary>A note represented as a node in the knowledge graph.</summary>
public class GraphNode
{
    /// <summary>The note ID.</summary>
    public required string Id { get; set; }
    /// <summary>The note title.</summary>
    public required string Title { get; set; }
    /// <summary>Number of edges connected to this node.</summary>
    public int EdgeCount { get; set; }
}

/// <summary>A relationship between two notes in the knowledge graph.</summary>
public class GraphEdge
{
    /// <summary>The ID of the source note.</summary>
    public required string Source { get; set; }
    /// <summary>The ID of the target note.</summary>
    public required string Target { get; set; }
    /// <summary>Edge type: "wikilink" (explicit [[link]]) or "semantic" (AI similarity).</summary>
    public required string Type { get; set; }
    /// <summary>Edge weight — cosine similarity for semantic edges, 1.0 for wikilinks.</summary>
    public double Weight { get; set; }
}
