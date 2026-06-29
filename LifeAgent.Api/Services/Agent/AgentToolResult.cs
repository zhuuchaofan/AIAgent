namespace LifeAgent.Api.Services.Agent;

public class AgentToolResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }

    public static AgentToolResult Ok(object output) => new()
    {
        Success = true,
        Output = output
    };

    public static AgentToolResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
