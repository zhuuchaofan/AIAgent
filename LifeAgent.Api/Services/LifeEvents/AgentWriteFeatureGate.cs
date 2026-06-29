using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.LifeEvents;

public class AgentWriteFeatureGate : IAgentWriteFeatureGate
{
    public AgentWriteFeatureGate(IConfiguration configuration)
    {
        Options = AgentWriteFeatureOptions.FromConfiguration(configuration);
    }

    public AgentWriteFeatureOptions Options { get; }

    public bool CanCreateLifeEvent()
    {
        return Options.CanCreateLifeEvent;
    }
}
