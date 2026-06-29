using LifeAgent.Api.Models;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

public interface IAgentLifeEventService
{
    Task<LifeEvent> CreateFromAgentConfirmationAsync(
        string authenticatedUserId,
        string agentActionId,
        CreateLifeEventRequest request,
        CancellationToken cancellationToken = default);
}
