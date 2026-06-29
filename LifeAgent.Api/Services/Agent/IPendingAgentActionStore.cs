using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public interface IPendingAgentActionStore
{
    PendingAgentAction Create(string userId, string actionType, string title, string summary, object payload, string riskLevel, TimeSpan ttl);
    AgentConfirmationResponse Confirm(string userId, string actionId, string decision);
}
