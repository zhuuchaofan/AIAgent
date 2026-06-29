namespace LifeAgent.Api.Models.Agent;

public class AgentToolCallResult
{
    public int Step { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = "success";
    public object? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}
