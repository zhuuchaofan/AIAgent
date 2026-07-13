namespace LifeAgent.Api.Services.Agent.Phase8;

public static class Phase80PersonalHomeIntentRouter
{
    public const string LifeRecordIntent = "life_record";
    public const string ReminderIntent = "reminder";
    public const string PlanIntent = "plan";
    public const string ToolActionIntent = "tool_action";
    public const string PendingConfirmationDisposition = "pending_confirmation";
    public const string DirectSaveDisposition = "direct_save";
    public const string RequiredConfirmationDisposition = "required_confirmation";
    public const string LowRisk = "low_record";
    public const string MediumRisk = "medium_user_state_change";
    public const string HighRisk = "high_external_or_irreversible";

    public static Phase80PersonalHomeIntentRoute Route(
        string title,
        string summary,
        string? requestedActionType,
        string classifier = "rule_based_intent_classifier")
    {
        var source = $"{title} {summary}";
        var actionType = NormalizeActionType(requestedActionType, source);
        var intent = ResolveIntent(actionType);
        var policy = Phase80PersonalHomeRoutingPolicy.DefaultPreviewOnly().Resolve(intent);
        return new Phase80PersonalHomeIntentRoute(
            Intent: intent,
            ActionType: actionType,
            Disposition: policy.Disposition,
            RiskLevel: policy.RiskLevel,
            RequiresPendingAction: policy.RequiresPendingAction,
            Reason: string.IsNullOrWhiteSpace(requestedActionType)
                ? "inferred_from_home_input"
                : "requested_action_type",
            Classifier: classifier);
    }

    private static string NormalizeActionType(string? actionType, string source)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return LooksLikeReminder(source)
                ? Phase80PendingActionRuntime.ReminderPreview
                : LooksLikePlan(source)
                    ? Phase80PendingActionRuntime.PlanPreview
                    : Phase80PendingActionRuntime.LifeRecordPreview;
        }

        if (string.Equals(actionType, Phase80PendingActionRuntime.LifeRecordPreview, StringComparison.OrdinalIgnoreCase))
        {
            return Phase80PendingActionRuntime.LifeRecordPreview;
        }

        if (string.Equals(actionType, Phase80PendingActionRuntime.ReminderPreview, StringComparison.OrdinalIgnoreCase))
        {
            return Phase80PendingActionRuntime.ReminderPreview;
        }

        if (string.Equals(actionType, Phase80PendingActionRuntime.PlanPreview, StringComparison.OrdinalIgnoreCase))
        {
            return Phase80PendingActionRuntime.PlanPreview;
        }

        return actionType.Trim();
    }

    private static string ResolveIntent(string actionType)
    {
        return actionType switch
        {
            Phase80PendingActionRuntime.LifeRecordPreview => LifeRecordIntent,
            Phase80PendingActionRuntime.ReminderPreview => ReminderIntent,
            Phase80PendingActionRuntime.PlanPreview => PlanIntent,
            _ => ToolActionIntent
        };
    }

    private static bool LooksLikeReminder(string value)
    {
        return value.Contains("提醒", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("闹钟", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("到点", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlan(string value)
    {
        return value.Contains("计划", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("安排", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("规划", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record Phase80PersonalHomeRoutingPolicy(bool AllowLowRiskDirectSave)
{
    public static Phase80PersonalHomeRoutingPolicy DefaultPreviewOnly()
    {
        return new Phase80PersonalHomeRoutingPolicy(AllowLowRiskDirectSave: false);
    }

    public Phase80PersonalHomeRoutingDecision Resolve(string intent)
    {
        return intent switch
        {
            Phase80PersonalHomeIntentRouter.LifeRecordIntent when AllowLowRiskDirectSave => new Phase80PersonalHomeRoutingDecision(
                Disposition: Phase80PersonalHomeIntentRouter.DirectSaveDisposition,
                RiskLevel: Phase80PersonalHomeIntentRouter.LowRisk,
                RequiresPendingAction: false),
            Phase80PersonalHomeIntentRouter.LifeRecordIntent => new Phase80PersonalHomeRoutingDecision(
                Disposition: Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition,
                RiskLevel: Phase80PersonalHomeIntentRouter.LowRisk,
                RequiresPendingAction: true),
            Phase80PersonalHomeIntentRouter.ReminderIntent => new Phase80PersonalHomeRoutingDecision(
                Disposition: Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition,
                RiskLevel: Phase80PersonalHomeIntentRouter.MediumRisk,
                RequiresPendingAction: true),
            Phase80PersonalHomeIntentRouter.PlanIntent => new Phase80PersonalHomeRoutingDecision(
                Disposition: Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition,
                RiskLevel: Phase80PersonalHomeIntentRouter.MediumRisk,
                RequiresPendingAction: true),
            _ => new Phase80PersonalHomeRoutingDecision(
                Disposition: Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition,
                RiskLevel: Phase80PersonalHomeIntentRouter.HighRisk,
                RequiresPendingAction: true)
        };
    }
}

internal sealed record Phase80PersonalHomeRoutingDecision(
    string Disposition,
    string RiskLevel,
    bool RequiresPendingAction);
