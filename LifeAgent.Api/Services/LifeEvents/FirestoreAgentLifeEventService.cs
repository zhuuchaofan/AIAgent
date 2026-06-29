using LifeAgent.Api.Models;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

public class FirestoreAgentLifeEventService : IAgentLifeEventService
{
    private readonly IAgentLifeEventWriter _writer;

    public FirestoreAgentLifeEventService(IAgentLifeEventWriter writer)
    {
        _writer = writer;
    }

    public async Task<LifeEvent> CreateFromAgentConfirmationAsync(
        string authenticatedUserId,
        string agentActionId,
        CreateLifeEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var lifeEvent = AgentLifeEventFactory.Create(authenticatedUserId, agentActionId, request);
        try
        {
            await _writer.WriteAsync(authenticatedUserId, lifeEvent.Id, lifeEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to write Agent life event.", ex);
        }

        return lifeEvent;
    }
}
