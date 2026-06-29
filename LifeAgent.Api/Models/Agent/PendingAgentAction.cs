namespace LifeAgent.Api.Models.Agent;

public class PendingAgentAction
{
    public string UserId { get; set; } = string.Empty;
    public AgentProposedAction ProposedAction { get; set; } = new();
    public bool Consumed { get; set; }
}
