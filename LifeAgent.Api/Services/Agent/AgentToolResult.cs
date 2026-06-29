namespace LifeAgent.Api.Services.Agent;

public class AgentToolResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? OutputSummary { get; init; }
    public string? ErrorMessage { get; init; }

    public static AgentToolResult Ok(object output, string? outputSummary = null) => new()
    {
        Success = true,
        Output = output,
        OutputSummary = outputSummary
    };

    public static AgentToolResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
