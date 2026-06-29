namespace LifeAgent.Api.Models.Agent;

public class AgentRunApiResponse
{
    public bool Success { get; set; } = true;
    public AgentRunResponse Data { get; set; } = new();
}
