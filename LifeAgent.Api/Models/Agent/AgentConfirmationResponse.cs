namespace LifeAgent.Api.Models.Agent;

public class AgentConfirmationResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionId { get; set; }
    public string? ActionType { get; set; }
    public object? Result { get; set; }
}
