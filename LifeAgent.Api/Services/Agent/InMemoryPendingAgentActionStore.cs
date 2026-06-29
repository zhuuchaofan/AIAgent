using System.Collections.Concurrent;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

// Test/local preview-only lifecycle store. Production uses
// FirestorePendingAgentActionStore so pending actions survive restarts.
public class InMemoryPendingAgentActionStore : IPendingAgentActionStore
{
    public const string Created = "created";
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    private static readonly HashSet<string> AllowedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_life_event",
        "save_memory_preview",
        "create_life_event_preview",
        "create_reminder_preview"
    };

    private readonly ConcurrentDictionary<string, PendingAgentAction> _actions = new();

    public Task<PendingAgentAction> CreateAsync(string userId, string actionType, string title, string summary, object payload, string riskLevel, TimeSpan ttl, CancellationToken cancellationToken = default)
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
            PreviewOnly = true,
            CreatedAt = now,
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
        return Task.FromResult(action);
    }

    public Task<PendingAgentAction?> GetAsync(string userId, string actionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) || !_actions.TryGetValue(actionId, out var pending))
        {
            return Task.FromResult<PendingAgentAction?>(null);
        }

        if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
        {
            return Task.FromResult<PendingAgentAction?>(null);
        }

        return Task.FromResult<PendingAgentAction?>(pending);
    }

    public Task<AgentConfirmationResponse> ConfirmAsync(string userId, string actionId, string decision, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) || !_actions.TryGetValue(actionId, out var pending))
        {
            return Task.FromResult(Failed(actionId, "not_found", "Pending action was not found."));
        }

        if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
        {
            return Task.FromResult(Failed(actionId, "not_found", "Pending action was not found."));
        }

        if (!AllowedActionTypes.Contains(pending.ProposedAction.ActionType))
        {
            return Task.FromResult(Failed(actionId, "invalid_action_type", "Unknown proposed action type."));
        }

        var normalizedDecision = (decision ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedDecision is not ("confirm" or "cancel"))
        {
            return Task.FromResult(Failed(actionId, "invalid_decision", "Decision must be confirm or cancel."));
        }

        if (pending.Status is Confirmed or Cancelled or Expired)
        {
            if (IsSameTerminalDecision(pending.Status, normalizedDecision))
            {
                return Task.FromResult(PreviewSuccess(pending, idempotent: true));
            }

            return Task.FromResult(Failed(actionId, pending.Status, $"Pending action is already {pending.Status}."));
        }

        if (pending.ProposedAction.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Transition(pending, Expired);
            return Task.FromResult(Failed(actionId, Expired, "Pending action expired."));
        }

        if (normalizedDecision == "cancel")
        {
            Transition(pending, Cancelled);
            return Task.FromResult(PreviewSuccess(pending, idempotent: false));
        }

        Transition(pending, Confirmed);
        return Task.FromResult(PreviewSuccess(pending, idempotent: false));
    }

    private static AgentConfirmationResponse PreviewSuccess(PendingAgentAction pending, bool idempotent)
    {
        var message = pending.Status == Cancelled
            ? "Agent action preview cancelled. No data was written."
            : "Agent action preview confirmed. No data was written in Phase 4.7.";

        return new AgentConfirmationResponse
        {
            Success = true,
            Status = pending.Status,
            Message = message,
            ActionId = pending.ProposedAction.ActionId,
            ActionType = pending.ProposedAction.ActionType,
            LifecycleStatus = pending.Status,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending.ProposedAction.ActionType,
                idempotent
            }
        };
    }

    private static bool IsSameTerminalDecision(string status, string decision)
    {
        return status == Confirmed && decision == "confirm" ||
               status == Cancelled && decision == "cancel";
    }

    private static void Transition(PendingAgentAction action, string status)
    {
        action.Status = status;
        action.UpdatedAt = DateTimeOffset.UtcNow;
        action.ProposedAction.LifecycleStatus = status;
        if (status == Confirmed)
        {
            action.ConfirmedAt = action.UpdatedAt;
        }
        else if (status == Cancelled)
        {
            action.CancelledAt = action.UpdatedAt;
        }
        else if (status == Expired)
        {
            action.ExpiredAt = action.UpdatedAt;
        }
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
