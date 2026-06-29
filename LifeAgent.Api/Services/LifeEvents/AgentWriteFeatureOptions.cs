using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.LifeEvents;

public class AgentWriteFeatureOptions
{
    public const string EnableAgentWriteToolsEnvName = "ENABLE_AGENT_WRITE_TOOLS";
    public const string EnableCreateLifeEventToolEnvName = "ENABLE_CREATE_LIFE_EVENT_TOOL";

    public bool EnableAgentWriteTools { get; init; }
    public bool EnableCreateLifeEventTool { get; init; }

    public bool CanCreateLifeEvent => EnableAgentWriteTools && EnableCreateLifeEventTool;

    public static AgentWriteFeatureOptions FromConfiguration(IConfiguration configuration)
    {
        return new AgentWriteFeatureOptions
        {
            EnableAgentWriteTools = IsTrue(configuration[EnableAgentWriteToolsEnvName]),
            EnableCreateLifeEventTool = IsTrue(configuration[EnableCreateLifeEventToolEnvName])
        };
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
