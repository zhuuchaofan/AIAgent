using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.LifeEvents;

namespace LifeAgent.Api.Services.Agent;

public sealed class AgentContractValidator
{
    private static readonly HashSet<string> AllowedProposedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AgentActionTypes.CreateLifeEvent,
        AgentActionTypes.CreateLifeEventPreview,
        AgentActionTypes.SaveMemoryPreview,
        AgentActionTypes.CreateReminderPreview
    };

    public AgentExecutionContract BuildContract(AgentIntentResolution resolution)
    {
        var actionType = resolution.Intent switch
        {
            AgentIntentNames.LifeEvent => AgentActionTypes.CreateLifeEvent,
            AgentIntentNames.Memory => AgentActionTypes.SaveMemoryPreview,
            AgentIntentNames.Reminder => AgentActionTypes.Reminder,
            AgentIntentNames.Rag => AgentActionTypes.ReadonlyRag,
            AgentIntentNames.Document => AgentActionTypes.Document,
            _ => AgentActionTypes.ReadonlyRag
        };

        var requiresConfirmation = resolution.Intent is
            AgentIntentNames.LifeEvent or
            AgentIntentNames.Memory or
            AgentIntentNames.Reminder;

        return new AgentExecutionContract(
            resolution.Intent,
            resolution.Confidence,
            actionType,
            requiresConfirmation,
            resolution.Intent == AgentIntentNames.Unknown,
            resolution.FallbackReason);
    }

    public AgentContractValidationResult Validate(AgentExecutionContract contract, AgentExecutionResult execution)
    {
        if (!IsAllowedActionType(contract.ActionType))
        {
            return AgentContractValidationResult.Invalid($"Unsupported action type: {contract.ActionType}");
        }

        if (execution.ProposedAction == null)
        {
            return AgentContractValidationResult.Valid();
        }

        var proposedAction = execution.ProposedAction;
        if (string.IsNullOrWhiteSpace(proposedAction.ActionType) ||
            !AllowedProposedActionTypes.Contains(proposedAction.ActionType))
        {
            return AgentContractValidationResult.Invalid($"Unsupported proposed action type: {proposedAction.ActionType}");
        }

        if (!proposedAction.RequiresConfirmation)
        {
            return AgentContractValidationResult.Invalid("Proposed actions must require confirmation in preview-only mode.");
        }

        if (!contract.RequiresConfirmation)
        {
            return AgentContractValidationResult.Invalid("Only confirmation intents may return proposedAction.");
        }

        if (proposedAction.ActionType.Equals(AgentActionTypes.CreateLifeEvent, StringComparison.OrdinalIgnoreCase) ||
            proposedAction.ActionType.Equals(AgentActionTypes.CreateLifeEventPreview, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var request = LifeEventActionPayloadMapper.Map(proposedAction.Payload);
                LifeEventPayloadValidator.ValidateAndSanitize(request);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return AgentContractValidationResult.Invalid(ex.Message);
            }
        }

        return AgentContractValidationResult.Valid();
    }

    public AgentContractValidationResult ValidatePendingConfirmation(PendingAgentAction? pending)
    {
        if (pending is null)
        {
            return AgentContractValidationResult.Valid();
        }

        if (!string.Equals(pending.Status, InMemoryPendingAgentActionStore.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return AgentContractValidationResult.Valid();
        }

        var actionType = pending.ProposedAction.ActionType;
        var intent = actionType switch
        {
            AgentActionTypes.CreateLifeEvent or AgentActionTypes.CreateLifeEventPreview => AgentIntentNames.LifeEvent,
            AgentActionTypes.SaveMemoryPreview => AgentIntentNames.Memory,
            AgentActionTypes.CreateReminderPreview => AgentIntentNames.Reminder,
            _ => AgentIntentNames.Unknown
        };
        var contract = new AgentExecutionContract(
            intent,
            1.0,
            actionType == AgentActionTypes.CreateReminderPreview ? AgentActionTypes.Reminder : actionType,
            RequiresConfirmation: true,
            IsFallback: false,
            FallbackReason: null);

        return Validate(contract, AgentExecutionResult.Confirmation(pending.ProposedAction));
    }

    private static bool IsAllowedActionType(string actionType)
    {
        return actionType is
            AgentActionTypes.CreateLifeEvent or
            AgentActionTypes.CreateLifeEventPreview or
            AgentActionTypes.SaveMemoryPreview or
            AgentActionTypes.Reminder or
            AgentActionTypes.CreateReminderPreview or
            AgentActionTypes.ReadonlyRag or
            AgentActionTypes.Document or
            AgentActionTypes.Invalid;
    }
}

public sealed record AgentContractValidationResult(bool Success, string? ErrorMessage)
{
    public static AgentContractValidationResult Valid()
    {
        return new AgentContractValidationResult(true, null);
    }

    public static AgentContractValidationResult Invalid(string errorMessage)
    {
        return new AgentContractValidationResult(false, errorMessage);
    }
}
