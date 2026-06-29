using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.Agent;

namespace LifeAgent.Api.Services.LifeEvents;

public class AgentLifeEventConfirmationWriteCoordinator
{
    private const string CreateLifeEventActionType = "create_life_event";
    private const string CreatedResourceType = "life_event";

    private readonly IPendingAgentActionStore _pendingActions;
    private readonly IAgentLifeEventService _lifeEventService;

    public AgentLifeEventConfirmationWriteCoordinator(
        IPendingAgentActionStore pendingActions,
        IAgentLifeEventService lifeEventService)
    {
        _pendingActions = pendingActions;
        _lifeEventService = lifeEventService;
    }

    public async Task<AgentConfirmationResponse> ConfirmCreateLifeEventAsync(
        string authenticatedUserId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var pending = await _pendingActions.GetAsync(authenticatedUserId, actionId, cancellationToken);
        var validation = ValidatePendingAction(pending, actionId);
        if (validation is not null)
        {
            return validation;
        }

        var confirmedPending = pending;
        ArgumentNullException.ThrowIfNull(confirmedPending);

        try
        {
            var payload = LifeEventActionPayloadMapper.Map(confirmedPending.ProposedAction.Payload);
            var lifeEvent = await _lifeEventService.CreateFromAgentConfirmationAsync(
                authenticatedUserId,
                confirmedPending.ProposedAction.ActionId,
                payload,
                cancellationToken);

            return await _pendingActions.ConfirmWriteCompletedAsync(
                authenticatedUserId,
                confirmedPending.ProposedAction.ActionId,
                CreatedResourceType,
                lifeEvent.Id,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Failed(confirmedPending.ProposedAction.ActionId, "invalid_payload", ex.Message, confirmedPending);
        }
        catch (InvalidOperationException ex)
        {
            return Failed(confirmedPending.ProposedAction.ActionId, "write_failed", ex.Message, confirmedPending);
        }
    }

    private static AgentConfirmationResponse? ValidatePendingAction(
        PendingAgentAction? pending,
        string actionId)
    {
        if (pending is null)
        {
            return Failed(actionId, "not_found", "Pending action was not found.");
        }

        if (!string.Equals(pending.ProposedAction.ActionType, CreateLifeEventActionType, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(pending.ProposedAction.ActionId, "invalid_action_type", "Pending action is not create_life_event.", pending);
        }

        if (pending.Status == InMemoryPendingAgentActionStore.Confirmed && pending.WriteCompleted && pending.WroteData)
        {
            return new AgentConfirmationResponse
            {
                Success = true,
                Status = pending.Status,
                Message = "Agent action confirmed and life_event was written.",
                ActionId = pending.ProposedAction.ActionId,
                ActionType = pending.ProposedAction.ActionType,
                LifecycleStatus = pending.Status,
                Result = new
                {
                    previewOnly = false,
                    wroteData = true,
                    actionType = pending.ProposedAction.ActionType,
                    createdResourceType = pending.CreatedResourceType,
                    createdResourceId = pending.CreatedResourceId,
                    idempotent = true
                }
            };
        }

        if (pending.Status is InMemoryPendingAgentActionStore.Cancelled or InMemoryPendingAgentActionStore.Expired)
        {
            return Failed(pending.ProposedAction.ActionId, pending.Status, $"Pending action is already {pending.Status}.", pending);
        }

        if (pending.Status == InMemoryPendingAgentActionStore.Confirmed)
        {
            return Failed(pending.ProposedAction.ActionId, InMemoryPendingAgentActionStore.Confirmed, "Pending action is already confirmed without a write result.", pending);
        }

        if (pending.ProposedAction.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Failed(pending.ProposedAction.ActionId, InMemoryPendingAgentActionStore.Expired, "Pending action expired.", pending);
        }

        return null;
    }

    private static AgentConfirmationResponse Failed(
        string? actionId,
        string status,
        string message,
        PendingAgentAction? pending = null)
    {
        return new AgentConfirmationResponse
        {
            Success = false,
            Status = status,
            Message = message,
            ActionId = actionId,
            ActionType = pending?.ProposedAction.ActionType,
            LifecycleStatus = pending?.Status,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending?.ProposedAction.ActionType
            }
        };
    }
}
