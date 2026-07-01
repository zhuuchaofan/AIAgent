namespace LifeAgent.Api.Services.Agent;

using LifeAgent.Api.Services.Memories;

public class AgentContext
{
    public string UserId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string ClientTimeZone { get; init; } = "UTC";
    public IReadOnlyList<string> SelectedDocumentIds { get; init; } = Array.Empty<string>();
    public int MaxIterations { get; init; } = 3;
    public MemoryRuntimeContext MemoryContext { get; init; } = MemoryRuntimeContext.Disabled();
}
