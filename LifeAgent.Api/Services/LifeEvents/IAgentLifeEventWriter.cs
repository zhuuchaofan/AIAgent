using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.LifeEvents;

public interface IAgentLifeEventWriter
{
    Task WriteAsync(
        string authenticatedUserId,
        string eventId,
        LifeEvent lifeEvent,
        CancellationToken cancellationToken = default);
}
