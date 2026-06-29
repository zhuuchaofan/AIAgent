namespace LifeAgent.Api.Models.Agent;

public class PendingAgentAction
{
    public string UserId { get; set; } = string.Empty;
    public AgentProposedAction ProposedAction { get; set; } = new();
    public string Status { get; set; } = "created";
    public bool PreviewOnly { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public string? CreatedResourceType { get; set; }
    public string? CreatedResourceId { get; set; }
    public bool WroteData { get; set; }
    public bool WriteCompleted { get; set; }
    public DateTimeOffset? WriteCompletedAt { get; set; }
}
