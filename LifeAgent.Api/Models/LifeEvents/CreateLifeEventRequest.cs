namespace LifeAgent.Api.Models.LifeEvents;

public class CreateLifeEventRequest
{
    public string? Id { get; set; }
    public string? UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? CreatedBy { get; set; }
    public string? AgentActionId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Dictionary<string, object?> StructuredData { get; set; } = new();
}
