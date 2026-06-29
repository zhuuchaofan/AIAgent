namespace LifeAgent.Api.Services.LifeEvents;

public interface IAgentWriteFeatureGate
{
    AgentWriteFeatureOptions Options { get; }
    bool CanCreateLifeEvent();
}
