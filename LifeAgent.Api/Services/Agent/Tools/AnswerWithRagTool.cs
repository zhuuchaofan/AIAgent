using System.Text.Json;

namespace LifeAgent.Api.Services.Agent.Tools;

public class AnswerWithRagTool : IAgentTool
{
    public string Name => "answer_with_rag";
    public string Description => "Preview placeholder for wrapping existing RAG answer flow.";
    public AgentToolRisk Risk => AgentToolRisk.Compute;
    public bool RequiresConfirmation => false;

    public Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(AgentToolResult.Ok(new
        {
            answer = "answer_with_rag is defined but not connected in Phase 4.1 preview.",
            citations = Array.Empty<object>(),
            citationIntegrity = "not_applicable",
            todo = "Wrap RagChatService only after confirming message persistence semantics for Agent mode."
        }));
    }
}
