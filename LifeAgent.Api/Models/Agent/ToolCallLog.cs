namespace LifeAgent.Api.Models.Agent;

public class ToolCallLog
{
    public int Step { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Risk { get; set; } = "read";
    public string Status { get; set; } = "success";
    public string? InputSummary { get; set; }
    public string? OutputSummary { get; set; }
    public bool RequiresConfirmation { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
