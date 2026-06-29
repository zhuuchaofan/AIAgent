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
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            throw new ArgumentException("authenticatedUserId is required.", nameof(authenticatedUserId));
        }

        if (string.IsNullOrWhiteSpace(agentActionId))
        {
            throw new ArgumentException("agentActionId is required.", nameof(agentActionId));
        }

        var structuredData = LifeEventPayloadValidator.ValidateAndSanitize(request);
        var now = DateTime.UtcNow;
        var lifeEvent = new LifeEvent
        {
            Id = $"evt_{Guid.NewGuid():N}",
            UserId = authenticatedUserId,
            Type = request.Type.Trim(),
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Source = "agent_confirmed",
            CreatedBy = "agent",
            AgentActionId = agentActionId,
            CreatedAt = now,
            UpdatedAt = now,
            OccurredAt = now,
            TimeZone = "UTC",
            StructuredData = structuredData,
            SchemaVersion = "v1",
            ReminderIntentDetected = false,
            ReminderParseStatus = "none",
            NeedsReview = false
        };

        _events.Add(lifeEvent);
        return Task.FromResult(lifeEvent);
    }
}
