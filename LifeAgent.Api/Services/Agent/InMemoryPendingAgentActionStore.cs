using System.Collections.Concurrent;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

// Phase 4 preview-only lifecycle store. Pending actions are lost on Cloud Run
// instance restart and are not shared across multiple instances. Phase 5 must
// replace this with a Firestore-backed store before real write tools or durable
// confirmation flows are enabled.
public class InMemoryPendingAgentActionStore : IPendingAgentActionStore
{
    public const string Created = "created";
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

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
            Status = Created,
            UpdatedAt = now,
            ProposedAction = new AgentProposedAction
            {
                ActionId = $"agent_action_{Guid.NewGuid():N}",
                ActionType = actionType,
                Title = title,
                Summary = summary,
                Payload = payload,
                RiskLevel = riskLevel,
                RequiresConfirmation = true,
                LifecycleStatus = Created,
                CreatedAt = now,
                ExpiresAt = now.Add(ttl)
            }
        };

        Transition(action, Pending);

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

        if (pending.Status is Confirmed or Cancelled or Expired)
        {
            return Failed(actionId, pending.Status, $"Pending action is already {pending.Status}.");
        }

        if (pending.ProposedAction.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Transition(pending, Expired);
            return Failed(actionId, Expired, "Pending action expired.");
        }

        var normalizedDecision = (decision ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedDecision == "cancel")
        {
            Transition(pending, Cancelled);
            return new AgentConfirmationResponse
            {
                Success = true,
                Status = Cancelled,
                Message = "Agent action preview cancelled. No data was written.",
                ActionId = actionId,
                ActionType = pending.ProposedAction.ActionType,
                LifecycleStatus = pending.Status
            };
        }

        if (normalizedDecision != "confirm")
        {
            return Failed(actionId, "invalid_decision", "Decision must be confirm or cancel.");
        }

        Transition(pending, Confirmed);
        return new AgentConfirmationResponse
        {
            Success = true,
            Status = Confirmed,
            Message = "Agent action preview confirmed. No data was written in Phase 4.6.",
            ActionId = actionId,
            ActionType = pending.ProposedAction.ActionType,
            LifecycleStatus = pending.Status,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending.ProposedAction.ActionType
            }
        };
    }

    private static void Transition(PendingAgentAction action, string status)
    {
        action.Status = status;
        action.UpdatedAt = DateTimeOffset.UtcNow;
        action.ProposedAction.LifecycleStatus = status;
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
