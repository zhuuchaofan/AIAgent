namespace LifeAgent.Api.Models.Agent;

public class AgentRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public string Message { get; set; } = string.Empty;
    public int MaxIterations { get; set; }
    public List<AgentToolCallResult> ToolCalls { get; set; } = new();
}
