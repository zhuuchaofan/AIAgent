using LifeAgent.Api.Models;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

public class InMemoryAgentLifeEventService : IAgentLifeEventService
{
    private readonly List<LifeEvent> _events = new();

    public IReadOnlyList<LifeEvent> Events => _events;

    public Task<LifeEvent> CreateFromAgentConfirmationAsync(
        string authenticatedUserId,
        string agentActionId,
        CreateLifeEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var lifeEvent = AgentLifeEventFactory.Create(authenticatedUserId, agentActionId, request);
        _events.Add(lifeEvent);
        return Task.FromResult(lifeEvent);
    }
}
