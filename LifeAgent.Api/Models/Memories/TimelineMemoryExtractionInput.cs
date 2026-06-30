namespace LifeAgent.Api.Models.Memories;

public sealed class TimelineMemoryExtractionInput
{
    public string EventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? OccurredAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
