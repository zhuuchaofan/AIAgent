using System.Collections.Concurrent;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

// Phase 4.6 preview-only store. Pending actions are lost on Cloud Run instance restart
// and are not shared across multiple instances; replace with Firestore-backed storage
// before enabling real write tools or durable confirmation flows.
public class InMemoryPendingAgentActionStore : IPendingAgentActionStore
{
    private static readonly HashSet<string> AllowedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "save_memory_preview",
        "create_life_event_preview",
        "create_reminder_preview"
    };

    private readonly ConcurrentDictionary<string, PendingAgentAction> _actions = new();

    public PendingAgentAction Create(string userId, string actionType, string title, string summary, object payload, string riskLevel, TimeSpan ttl)
    {
        if (!AllowedActionTypes.Contains(actionType))
        {
            throw new InvalidOperationException($"Unknown proposed action type: {actionType}");
        }

        var now = DateTimeOffset.UtcNow;
        var action = new PendingAgentAction
        {
            UserId = userId,
            ProposedAction = new AgentProposedAction
            {
                ActionId = $"agent_action_{Guid.NewGuid():N}",
                ActionType = actionType,
                Title = title,
                Summary = summary,
                Payload = payload,
                RiskLevel = riskLevel,
                RequiresConfirmation = true,
                CreatedAt = now,
                ExpiresAt = now.Add(ttl)
            }
        };

        _actions[action.ProposedAction.ActionId] = action;
        return action;
    }

    public AgentConfirmationResponse Confirm(string userId, string actionId, string decision)
    {
        if (string.IsNullOrWhiteSpace(actionId) || !_actions.TryGetValue(actionId, out var pending))
        {
            return Failed(actionId, "not_found", "Pending action was not found.");
        }

        if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
        {
            return Failed(actionId, "not_found", "Pending action was not found.");
        }

        if (!AllowedActionTypes.Contains(pending.ProposedAction.ActionType))
        {
            return Failed(actionId, "invalid_action_type", "Unknown proposed action type.");
        }

        if (pending.Consumed)
        {
            return Failed(actionId, "already_resolved", "Pending action was already resolved.");
        }

        if (pending.ProposedAction.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            pending.Consumed = true;
            return Failed(actionId, "expired", "Pending action expired.");
        }

        var normalizedDecision = decision.Trim().ToLowerInvariant();
        if (normalizedDecision == "cancel")
        {
            pending.Consumed = true;
            return new AgentConfirmationResponse
            {
                Success = true,
                Status = "cancelled",
                Message = "Agent action preview cancelled. No data was written.",
                ActionId = actionId,
                ActionType = pending.ProposedAction.ActionType
            };
        }

        if (normalizedDecision != "confirm")
        {
            return Failed(actionId, "invalid_decision", "Decision must be confirm or cancel.");
        }

        pending.Consumed = true;
        return new AgentConfirmationResponse
        {
            Success = true,
            Status = "preview_success",
            Message = "Agent action preview confirmed. No data was written in Phase 4.6.",
            ActionId = actionId,
            ActionType = pending.ProposedAction.ActionType,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending.ProposedAction.ActionType
            }
        };
    }

    private static AgentConfirmationResponse Failed(string? actionId, string status, string message)
    {
        return new AgentConfirmationResponse
        {
            Success = false,
            Status = status,
            Message = message,
            ActionId = actionId
        };
    }
}
