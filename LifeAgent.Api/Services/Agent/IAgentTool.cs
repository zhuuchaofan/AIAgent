using System.Text.Json;

namespace LifeAgent.Api.Services.Agent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    AgentToolRisk Risk { get; }
    bool RequiresConfirmation { get; }

    Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken);
}
