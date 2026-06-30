namespace LifeAgent.Api.Models.Agent;

public class AgentRunResponse
{
    public string RunId { get; set; } = string.Empty;
    public string Mode { get; set; } = "preview_readonly";
    public string Answer { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; }
    public AgentProposedAction? ProposedAction { get; set; }
    public string ActionType { get; set; } = "preview_readonly_rag";
    public bool PreviewOnly { get; set; } = true;
    public object Payload { get; set; } = new { };
    public bool WroteData { get; set; }
    public string? CreatedResourceId { get; set; }
    public List<LifeAgent.Api.Models.CitationNode> Citations { get; set; } = new();
    public string? CitationIntegrity { get; set; }
    public int MaxSteps { get; set; }
    public int StepsUsed { get; set; }
    public List<AgentToolCallResult> ToolCalls { get; set; } = new();
}
