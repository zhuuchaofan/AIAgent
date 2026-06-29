namespace LifeAgent.Api.Models.Agent;

public class AgentConfirmationRequest
{
    public string ActionId { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? UserId { get; set; }
}
