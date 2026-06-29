using LifeAgent.Api.Models;
using LifeAgent.Api.Models.LifeEvents;

namespace LifeAgent.Api.Services.LifeEvents;

internal static class AgentLifeEventFactory
{
    public static LifeEvent Create(
        string authenticatedUserId,
        string agentActionId,
        CreateLifeEventRequest request)
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
        return new LifeEvent
        {
            Id = CreateStableEventId(agentActionId),
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
    }

    private static string CreateStableEventId(string agentActionId)
    {
        var safeActionId = new string(agentActionId
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray());

        return $"evt_{safeActionId}";
    }
}
