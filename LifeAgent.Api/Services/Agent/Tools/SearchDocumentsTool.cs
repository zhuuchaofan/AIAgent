using System.Text.Json;

namespace LifeAgent.Api.Services.Agent.Tools;

public class SearchDocumentsTool : IAgentTool
{
    public string Name => "search_documents";
    public string Description => "Preview placeholder for read-only document vector search.";
    public AgentToolRisk Risk => AgentToolRisk.Read;
    public bool RequiresConfirmation => false;

    public Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(AgentToolResult.Ok(new
        {
            chunks = Array.Empty<object>(),
            todo = "Real vector search tool is intentionally not wired in Phase 4.1 to avoid changing the RAG main path."
        }));
    }
}
