namespace LifeAgent.Api.Models.Agent;

public class AgentToolCallResult
{
    public int Step { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    public string CapabilityType { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string Status { get; set; } = "success";
    public object? Input { get; set; }
    public object? Output { get; set; }
    public string? OutputSummary { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public string? TraceId { get; set; }
    public bool TraceRequired { get; set; }
    public bool AuditRequired { get; set; }
    public bool NoWrite { get; set; } = true;
    public bool WritesData { get; set; }
    public bool ExternalSideEffect { get; set; }
    public bool PendingActionCreated { get; set; }
    public bool ConfirmationRequired { get; set; }
}
