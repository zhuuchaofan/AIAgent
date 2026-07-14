using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed partial class Phase80PendingActionRuntime
{
    private Phase80PendingActionView ToView(
        PendingActionRecord record,
        Phase80ConfirmWriteExecutionResult? executionResult = null)
    {
        var status = ToPhase80Status(record.Status);
        var confirmPlan = ResolveStoredConfirmPlan(record);
        var confirmWriteDecision = ResolveConfirmWriteDecision(confirmPlan, _confirmWriteExecutor);
        var memoryPlan = ResolveStoredMemoryPlan(record);
        var route = ResolveStoredRoute(record);
        return new Phase80PendingActionView(
            ActionId: record.PendingActionId,
            Status: status,
            Title: ReadPayload(record, "title", "保存一条测试生活记录"),
            Summary: ReadPayload(record, "summary", "Phase 8 fake-first 待确认动作。"),
            ActionType: record.ActionType,
            Intent: route.Intent,
            Disposition: route.Disposition,
            RiskLevel: route.RiskLevel,
            RequiresPendingAction: route.RequiresPendingAction,
            RouteReason: route.Reason,
            IntentClassifier: route.Classifier,
            CreatedAt: record.CreatedAt,
            UpdatedAt: record.UpdatedAt,
            ExpiresAt: record.ExpiresAt,
            ConfirmedAt: status == Confirmed ? record.UpdatedAt : null,
            CancelledAt: status == Cancelled ? record.UpdatedAt : null,
            Executed: executionResult?.Success ?? record.Executed,
            WroteData: executionResult?.WroteData ?? record.WroteData,
            ExecutionReady: executionResult?.Success ?? record.Executed,
            GuardDecision: GuardDecision,
            SafetyMode: _safetyMode,
            LegacyConfirmEndpointUsed: false,
            RealWritePath: executionResult?.RealWritePath ?? record.WroteData,
            IsArchived: record.IsArchived,
            ConfirmTarget: confirmPlan.Target,
            ConfirmWriteEnabled: confirmPlan.WriteEnabled,
            ConfirmWriteExecutionReady: confirmWriteDecision.ExecutionReady,
            ConfirmWriteRealPathReady: confirmWriteDecision.RealPathReady,
            ConfirmWriteExecutorId: confirmWriteDecision.ExecutorId,
            ConfirmWriteDecisionReason: confirmWriteDecision.Reason,
            MemoryCandidateOnly: confirmPlan.MemoryCandidateOnly,
            ConfirmPlanReason: confirmPlan.Reason,
            MemoryTarget: memoryPlan.Target,
            MemoryWriteEnabled: memoryPlan.WriteEnabled,
            MemoryRequiresDedupe: memoryPlan.RequiresDedupe,
            MemoryRequiresMerge: memoryPlan.RequiresMerge,
            MemoryRequiresConfirmation: memoryPlan.RequiresConfirmation,
            Message: status switch
            {
                Confirmed when (executionResult?.WroteData ?? record.WroteData) => ConfirmedWrittenMessage(record.ActionType),
                Confirmed when executionResult?.Status == "missing_due_time" => "还缺少明确时间，暂未保存为提醒事项。",
                Confirmed when executionResult?.Status == "invalid_due_time" => "提醒时间暂时无法识别，未保存为提醒事项。",
                Confirmed => ConfirmedPreviewMessage(record.ActionType),
                Cancelled => "已取消",
                Expired => "已过期，不能确认",
                _ => "待确认"
            });
    }

    private Phase80ConfirmExecutionRequest ToConfirmExecutionRequest(PendingActionRecord record)
    {
        return new Phase80ConfirmExecutionRequest(
            UserId: record.UserSubjectRef,
            ActionId: record.PendingActionId,
            ActionType: record.ActionType,
            Title: ReadPayload(record, "title", "保存一条测试生活记录"),
            Summary: ReadPayload(record, "summary", "Phase 8 fake-first 待确认动作。"),
            Plan: ResolveStoredConfirmPlan(record),
            ClientTimeZone: ReadPayload(record, "clientTimeZone", "Asia/Shanghai"));
    }

    private static string ReadPayload(PendingActionRecord record, string key, string fallback)
    {
        return record.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static Phase80PersonalHomeIntentRoute ResolveStoredRoute(PendingActionRecord record)
    {
        var fallback = Phase80PersonalHomeIntentRouter.Route(
            ReadPayload(record, "title", string.Empty),
            ReadPayload(record, "summary", string.Empty),
            record.ActionType);

        return fallback with
        {
            Intent = ReadPayload(record, "intent", fallback.Intent),
            Disposition = ReadPayload(record, "disposition", fallback.Disposition),
            RiskLevel = string.IsNullOrWhiteSpace(record.RiskLevel) ? fallback.RiskLevel : record.RiskLevel,
            RequiresPendingAction = ReadBoolPayload(record, "requiresPendingAction", fallback.RequiresPendingAction),
            Reason = ReadMetadata(record, "routeReason", fallback.Reason),
            Classifier = ReadMetadata(record, "intentClassifier", fallback.Classifier)
        };
    }

    private static string ReadMetadata(PendingActionRecord record, string key, string fallback)
    {
        return record.RedactionMetadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private Phase80ConfirmExecutionPlan ResolveStoredConfirmPlan(PendingActionRecord record)
    {
        var fallback = ResolveConfirmExecutionPlan(record.ActionType, _confirmWritePolicy);
        return fallback with
        {
            Target = ReadPayload(record, "confirmTarget", fallback.Target),
            WriteEnabled = ReadBoolPayload(record, "confirmWriteEnabled", fallback.WriteEnabled),
            MemoryCandidateOnly = ReadBoolPayload(record, "memoryCandidateOnly", fallback.MemoryCandidateOnly),
            Reason = ReadPayload(record, "confirmPlanReason", fallback.Reason)
        };
    }

    private static Phase80MemoryPlan ResolveStoredMemoryPlan(PendingActionRecord record)
    {
        var fallback = ResolveMemoryPlan(record.ActionType);
        return fallback with
        {
            Target = ReadPayload(record, "memoryTarget", fallback.Target),
            WriteEnabled = ReadBoolPayload(record, "memoryWriteEnabled", fallback.WriteEnabled),
            RequiresDedupe = ReadBoolPayload(record, "memoryRequiresDedupe", fallback.RequiresDedupe),
            RequiresMerge = ReadBoolPayload(record, "memoryRequiresMerge", fallback.RequiresMerge),
            RequiresConfirmation = ReadBoolPayload(record, "memoryRequiresConfirmation", fallback.RequiresConfirmation)
        };
    }

    private static bool ReadBoolPayload(PendingActionRecord record, string key, bool fallback)
    {
        return record.Payload.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string ConfirmedPreviewMessage(string actionType)
    {
        return actionType switch
        {
            LifeRecordPreview => "已确认生活记录；当前未写入 life_events，也未执行真实操作。",
            ReminderPreview => "已确认提醒；当前仍未写入 reminders，也未执行真实操作。",
            PlanPreview => "已确认计划；当前仍未写入计划数据，也未执行真实操作。",
            _ => "已确认，但未执行；没有写入数据，也没有执行真实操作。"
        };
    }

    private static string ConfirmedWrittenMessage(string actionType)
    {
        return actionType switch
        {
            LifeRecordPreview => "已确认生活记录，并写入 life_events。",
            ReminderPreview => "已确认提醒，并写入 reminders。",
            _ => ConfirmedPreviewMessage(actionType)
        };
    }

    private static string ToPhase80Status(string status)
    {
        return status switch
        {
            PendingActionStatus.Confirmed => Confirmed,
            PendingActionStatus.Cancelled => Cancelled,
            PendingActionStatus.Expired => Expired,
            _ => Pending
        };
    }

    private static string ToPendingActionStatus(string status)
    {
        return status switch
        {
            Confirmed => PendingActionStatus.Confirmed,
            Cancelled => PendingActionStatus.Cancelled,
            Expired => PendingActionStatus.Expired,
            _ => PendingActionStatus.ConfirmationRequired
        };
    }
}
