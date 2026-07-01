using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryContextRequest
{
    public string UserId { get; init; } = string.Empty;
    public AgentRunRequest AgentRequest { get; init; } = new();
    public string Intent { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
}
