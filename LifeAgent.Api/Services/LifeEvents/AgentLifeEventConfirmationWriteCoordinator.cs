using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.Agent;

namespace LifeAgent.Api.Services.LifeEvents;

public class AgentLifeEventConfirmationWriteCoordinator
{
    private const string CreateLifeEventActionType = "create_life_event";
    private const string CreatedResourceType = "life_event";

    private readonly IPendingAgentActionStore _pendingActions;
    private readonly IAgentLifeEventService _lifeEventService;
    private readonly ILogger<AgentLifeEventConfirmationWriteCoordinator> _logger;

    public AgentLifeEventConfirmationWriteCoordinator(
        IPendingAgentActionStore pendingActions,
        IAgentLifeEventService lifeEventService,
        ILogger<AgentLifeEventConfirmationWriteCoordinator> logger)
    {
        _pendingActions = pendingActions;
        _lifeEventService = lifeEventService;
        _logger = logger;
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
            LogResponse(
                "Agent life_event write coordinator rejected pending action.",
                authenticatedUserId,
                validation);
            return validation;
        }

        var confirmedPending = pending;
        ArgumentNullException.ThrowIfNull(confirmedPending);

        try
        {
            _logger.LogInformation(
                "Agent life_event write started. UserId={UserId}, ActionId={ActionId}, ActionType={ActionType}, LifecycleStatus={LifecycleStatus}",
                authenticatedUserId,
                confirmedPending.ProposedAction.ActionId,
                confirmedPending.ProposedAction.ActionType,
                confirmedPending.Status);
            var payload = LifeEventActionPayloadMapper.Map(confirmedPending.ProposedAction.Payload);
            var lifeEvent = await _lifeEventService.CreateFromAgentConfirmationAsync(
                authenticatedUserId,
                confirmedPending.ProposedAction.ActionId,
                payload,
                cancellationToken);

            var response = await _pendingActions.ConfirmWriteCompletedAsync(
                authenticatedUserId,
                confirmedPending.ProposedAction.ActionId,
                CreatedResourceType,
                lifeEvent.Id,
                cancellationToken);
            LogResponse(
                "Agent life_event write completed.",
                authenticatedUserId,
                response);
            return response;
        }
        catch (ArgumentException ex)
        {
            var response = Failed(confirmedPending.ProposedAction.ActionId, "invalid_payload", ex.Message, confirmedPending);
            LogResponse(
                "Agent life_event write invalid_payload.",
                authenticatedUserId,
                response);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var response = Failed(confirmedPending.ProposedAction.ActionId, "write_failed", ex.Message, confirmedPending);
            LogResponse(
                "Agent life_event write write_failed.",
                authenticatedUserId,
                response);
            return response;
        }
    }

    private void LogResponse(
        string message,
        string userId,
        AgentConfirmationResponse response)
    {
        var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result ?? new { });
        var previewOnly = ReadBool(result, "previewOnly");
        var wroteData = ReadBool(result, "wroteData");
        var createdResourceType = ReadString(result, "createdResourceType");
        var createdResourceId = ReadString(result, "createdResourceId");
        var idempotent = ReadBool(result, "idempotent");

        _logger.LogInformation(
            "{Message} UserId={UserId}, ActionId={ActionId}, ActionType={ActionType}, ErrorCode={ErrorCode}, LifecycleStatus={LifecycleStatus}, PreviewOnly={PreviewOnly}, WroteData={WroteData}, CreatedResourceType={CreatedResourceType}, CreatedResourceId={CreatedResourceId}, Idempotent={Idempotent}",
            message,
            userId,
            response.ActionId,
            response.ActionType,
            response.Status,
            response.LifecycleStatus,
            previewOnly,
            wroteData,
            createdResourceType,
            createdResourceId,
            idempotent);
    }

    private static bool? ReadBool(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.ValueKind == System.Text.Json.JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.ValueKind == System.Text.Json.JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
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
