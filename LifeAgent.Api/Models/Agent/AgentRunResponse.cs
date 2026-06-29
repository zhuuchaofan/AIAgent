namespace LifeAgent.Api.Models.Agent;

public class AgentRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Mode { get; set; } = "preview_readonly";
    public string Answer { get; set; } = string.Empty;
    public int MaxSteps { get; set; }
    public int StepsUsed { get; set; }
    public List<AgentToolCallResult> ToolCalls { get; set; } = new();
}
