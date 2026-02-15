namespace ZettelWeb.Models;

public class GraphData
{
    public IReadOnlyList<GraphNode> Nodes { get; set; } = Array.Empty<GraphNode>();
    public IReadOnlyList<GraphEdge> Edges { get; set; } = Array.Empty<GraphEdge>();
}

public class GraphNode
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public int EdgeCount { get; set; }
}

public class GraphEdge
{
    public required string Source { get; set; }
    public required string Target { get; set; }
    public required string Type { get; set; } // "wikilink" or "semantic"
    public double Weight { get; set; }
}
