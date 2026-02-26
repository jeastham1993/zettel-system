using System.Text.Json.Serialization;

namespace ZettelWeb.Models;

public enum ResearchAgendaStatus { Pending, Approved, Executing, Completed, Cancelled, Failed }
public enum ResearchTaskStatus { Pending, Blocked, Completed, Failed }
public enum ResearchFindingStatus { Pending, Accepted, Dismissed }
public enum ResearchSourceType { WebSearch, Arxiv }

public class ResearchAgenda
{
    public required string Id { get; set; }
    public string? TriggeredFromNoteId { get; set; }
    public ResearchAgendaStatus Status { get; set; } = ResearchAgendaStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public List<ResearchTask> Tasks { get; set; } = new();
}

public class ResearchTask
{
    public required string Id { get; set; }
    public required string AgendaId { get; set; }
    public required string Query { get; set; }
    public ResearchSourceType SourceType { get; set; }
    public required string Motivation { get; set; }
    public string? MotivationNoteId { get; set; }
    public ResearchTaskStatus Status { get; set; } = ResearchTaskStatus.Pending;
    public DateTime? BlockedAt { get; set; }
    public List<ResearchFinding> Findings { get; set; } = new();
}

public class ResearchFinding
{
    public required string Id { get; set; }
    public required string TaskId { get; set; }
    public required string Title { get; set; }
    public required string Synthesis { get; set; }
    public required string SourceUrl { get; set; }
    public ResearchSourceType SourceType { get; set; }
    public List<string> SimilarNoteIds { get; set; } = new();
    public double? DuplicateSimilarity { get; set; }
    public ResearchFindingStatus Status { get; set; } = ResearchFindingStatus.Pending;
    public string? AcceptedFleetingNoteId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
