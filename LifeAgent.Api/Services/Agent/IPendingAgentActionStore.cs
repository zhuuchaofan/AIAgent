using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public interface IPendingAgentActionStore
{
    Task<PendingAgentAction> CreateAsync(string userId, string actionType, string title, string summary, object payload, string riskLevel, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<PendingAgentAction?> GetAsync(string userId, string actionId, CancellationToken cancellationToken = default);
    Task<AgentConfirmationResponse> ConfirmAsync(string userId, string actionId, string decision, CancellationToken cancellationToken = default);
    Task<AgentConfirmationResponse> ConfirmWriteCompletedAsync(
        string userId,
        string actionId,
        string createdResourceType,
        string createdResourceId,
        CancellationToken cancellationToken = default);
}
