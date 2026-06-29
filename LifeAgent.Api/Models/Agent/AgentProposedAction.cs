namespace LifeAgent.Api.Models.Agent;

public class AgentProposedAction
{
    public string ActionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public object Payload { get; set; } = new { };
    public string RiskLevel { get; set; } = "medium";
    public bool RequiresConfirmation { get; set; } = true;
    public string LifecycleStatus { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
