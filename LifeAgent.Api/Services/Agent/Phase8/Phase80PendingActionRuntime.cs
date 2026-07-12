using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.LifeEvents;

namespace LifeAgent.Api.Services.Agent.Phase8;

// Name retained for compatibility with Phase 8/9 tests and docs. This is now
// the LifeOS Personal Home pending action runtime mainline.
public sealed class Phase80PendingActionRuntime
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string LifeRecordPreview = "life_record_preview";
    public const string ReminderPreview = "reminder_preview";
    public const string PlanPreview = "plan_preview";
    public const string SafetyMode = "personal_agent_v2_in_memory_preview_only";
    public const string GuardDecision = "deny_all_no_real_execution";
    public const string ConfirmTargetLifeEvents = "life_events";
    public const string ConfirmTargetReminders = "reminders";
    public const string ConfirmTargetNone = "none";
    public const string MemoryCandidateTarget = "memory_candidate";

    private readonly IPendingActionStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly string _safetyMode;
    private readonly Phase80ConfirmWritePolicy _confirmWritePolicy;
    private readonly IPhase80ConfirmWriteExecutor _confirmWriteExecutor;
    private readonly bool _enableConfirmWriteExecution;

    public Phase80PendingActionRuntime(
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        IPendingActionStore? store = null,
        string? safetyMode = null,
        Phase80ConfirmWritePolicy? confirmWritePolicy = null,
        IPhase80ConfirmWriteExecutor? confirmWriteExecutor = null,
        bool enableConfirmWriteExecution = false)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _store = store ?? new InMemoryPendingActionStore(_timeProvider);
        _safetyMode = string.IsNullOrWhiteSpace(safetyMode) ? SafetyMode : safetyMode;
        _confirmWritePolicy = confirmWritePolicy ?? Phase80ConfirmWritePolicy.DefaultPreviewOnly();
        _confirmWriteExecutor = confirmWriteExecutor ?? Phase80NoOpConfirmWriteExecutor.Instance;
        _enableConfirmWriteExecution = enableConfirmWriteExecution;
    }

    public Phase80PendingActionResult Create(string userId, Phase80CreatePendingActionRequest? request)
    {
        return CreateAsync(userId, request).GetAwaiter().GetResult();
    }

    public async Task<Phase80PendingActionResult> CreateAsync(
        string userId,
        Phase80CreatePendingActionRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Phase80PendingActionResult.Fail("unauthorized", "User ID is missing from security context.");
        }

        var now = _timeProvider.GetUtcNow();
        var title = string.IsNullOrWhiteSpace(request?.Title)
            ? "保存一条测试生活记录"
            : request.Title.Trim();
        var summary = string.IsNullOrWhiteSpace(request?.Summary)
            ? "Phase 8.0 fake-first 待确认动作：确认后只改变状态，不写入 Firestore，也不执行真实 tool。"
            : request.Summary.Trim();
        var route = Phase80PersonalHomeIntentRouter.Route(title, summary, request?.ActionType);
        var actionType = route.ActionType;
        var confirmPlan = ResolveConfirmExecutionPlan(actionType, _confirmWritePolicy);
        var memoryPlan = ResolveMemoryPlan(actionType);

        var actionId = $"phase8_action_{Guid.NewGuid():N}";
        var created = await _store.CreateAsync(new PendingActionCreateRequest(
            PendingActionId: actionId,
            PreviewId: $"preview_{actionId}",
            ToolId: "phase8_preview_tool",
            ToolVersion: "1.0",
            AdapterId: "phase8_preview_adapter",
            ActionType: actionType,
            UserSubjectRef: userId,
            SessionSubjectRef: "agent_preview_default_session",
            RiskLevel: route.RiskLevel,
            ExpiresAt: now.Add(_ttl),
            IdempotencyKeyHash: $"phase8_idem_{actionId}",
            InputHash: $"phase8_input_{actionId}",
            PreviewHash: $"phase8_preview_{actionId}",
            PolicySnapshotRef: "phase8_preview_policy_default",
            TraceId: $"phase8_trace_{actionId}",
            AuditEventRefs: new[] { $"phase8_audit_{actionId}_created" },
            SanitizedPreviewRef: $"phase8_sanitized_preview_{actionId}",
            ServerOnlyPayloadRef: $"phase8_payload_ref_{actionId}",
            Payload: new Dictionary<string, string>
            {
                ["title"] = title,
                ["summary"] = summary,
                ["actionType"] = actionType,
                ["intent"] = route.Intent,
                ["disposition"] = route.Disposition,
                ["requiresPendingAction"] = route.RequiresPendingAction.ToString(),
                ["confirmTarget"] = confirmPlan.Target,
                ["confirmWriteEnabled"] = confirmPlan.WriteEnabled.ToString(),
                ["memoryCandidateOnly"] = confirmPlan.MemoryCandidateOnly.ToString(),
                ["confirmPlanReason"] = confirmPlan.Reason,
                ["memoryTarget"] = memoryPlan.Target,
                ["memoryWriteEnabled"] = memoryPlan.WriteEnabled.ToString(),
                ["memoryRequiresDedupe"] = memoryPlan.RequiresDedupe.ToString(),
                ["memoryRequiresMerge"] = memoryPlan.RequiresMerge.ToString(),
                ["memoryRequiresConfirmation"] = memoryPlan.RequiresConfirmation.ToString()
            },
            RedactionMetadata: new Dictionary<string, string>
            {
                ["mode"] = "preview_only",
                ["routeReason"] = route.Reason
            },
            ValidationSnapshot: new Dictionary<string, string>
            {
                ["guardDecision"] = GuardDecision
            }),
            cancellationToken);

        if (!created.Success || created.Record is null)
        {
            return Phase80PendingActionResult.Fail(
                created.Status,
                created.Message ?? "Pending action could not be created.");
        }

        return Phase80PendingActionResult.Ok(
            "pending",
            "已生成待确认动作。",
            ToView(created.Record));
    }

    public IReadOnlyList<Phase80PendingActionView> List(string userId)
    {
        return ListAsync(userId).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<Phase80PendingActionView>> ListAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<Phase80PendingActionView>();
        }

        var records = await _store.QueryAsync(new PendingActionQuery(userId), cancellationToken);
        return records.Select(record => ToView(record)).ToArray();
    }

    public async Task<Phase80PendingActionResult> ArchiveAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Phase80PendingActionResult.Fail("unauthorized", "User ID is missing from security context.");
        }

        var archived = await _store.ArchiveAsync(
            userId,
            actionId,
            auditEventRef: $"phase8_audit_{actionId}_archived",
            cancellationToken);

        if (!archived.Success || archived.Record is null)
        {
            return Phase80PendingActionResult.Fail(
                archived.Status,
                archived.Message ?? "Pending action could not be hidden.");
        }

        return Phase80PendingActionResult.Ok(
            archived.Record.Status,
            "已隐藏该历史记录；没有删除 Firestore 文档。",
            ToView(archived.Record));
    }

    public Phase80PendingActionResult Confirm(string userId, string actionId)
    {
        return ConfirmAsync(userId, actionId).GetAwaiter().GetResult();
    }

    public async Task<Phase80PendingActionResult> ConfirmAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var current = await _store.GetByIdAsync(userId, actionId, cancellationToken);
        if (current is null)
        {
            return Phase80PendingActionResult.Fail("not_found", "Pending action was not found.");
        }

        var status = ToPhase80Status(current.Status);
        if (status == Expired)
        {
            return Phase80PendingActionResult.Fail(Expired, "Pending action expired.", ToView(current));
        }

        if (status == Cancelled)
        {
            return Phase80PendingActionResult.Fail(Cancelled, "Cancelled pending action cannot be confirmed.", ToView(current));
        }

        if (status == Confirmed)
        {
            return Phase80PendingActionResult.Ok(
                Confirmed,
                "该动作已经确认，但仍未执行。",
                ToView(current));
        }

        var executionResult = _enableConfirmWriteExecution
            ? await TryExecuteConfirmWriteAsync(
                ToConfirmExecutionRequest(current),
                ResolveConfirmWriteDecision(ResolveStoredConfirmPlan(current), _confirmWriteExecutor),
                cancellationToken)
            : null;

        var confirmed = await _store.UpdateStatusAsync(new PendingActionStatusUpdate(
            PendingActionId: actionId,
            UserSubjectRef: userId,
            ExpectedStatus: PendingActionStatus.ConfirmationRequired,
            NewStatus: PendingActionStatus.Confirmed,
            ConfirmationId: $"phase8_confirmation_{actionId}",
            AuditEventRef: executionResult?.Success == true
                ? $"phase8_audit_{actionId}_confirmed_write_completed"
                : $"phase8_audit_{actionId}_confirmed",
            WroteData: executionResult?.Success == true && executionResult.WroteData,
            Executed: executionResult?.Success == true),
            cancellationToken);

        if (!confirmed.Success || confirmed.Record is null)
        {
            return Phase80PendingActionResult.Fail(
                confirmed.Status,
                confirmed.Message ?? "Pending action could not be confirmed.",
                confirmed.Record is null ? null : ToView(confirmed.Record));
        }

        var viewRecord = confirmed.Record;

        return Phase80PendingActionResult.Ok(
            Confirmed,
            executionResult?.Success == true
                ? "已确认并完成 Beta 测试写入。"
                : ConfirmedPreviewMessage(confirmed.Record.ActionType),
            ToView(viewRecord, executionResult));
    }

    public Phase80PendingActionResult Cancel(string userId, string actionId)
    {
        return CancelAsync(userId, actionId).GetAwaiter().GetResult();
    }

    public async Task<Phase80PendingActionResult> CancelAsync(
        string userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var current = await _store.GetByIdAsync(userId, actionId, cancellationToken);
        if (current is null)
        {
            return Phase80PendingActionResult.Fail("not_found", "Pending action was not found.");
        }

        var status = ToPhase80Status(current.Status);
        if (status == Expired)
        {
            return Phase80PendingActionResult.Fail(Expired, "Pending action expired.", ToView(current));
        }

        if (status == Confirmed)
        {
            return Phase80PendingActionResult.Fail(Confirmed, "Confirmed pending action cannot be cancelled in Phase 8.0.", ToView(current));
        }

        if (status == Cancelled)
        {
            return Phase80PendingActionResult.Ok(
                Cancelled,
                "该动作已经取消。",
                ToView(current));
        }

        var updated = await _store.CancelAsync(
            userId,
            actionId,
            cancellationReason: "user_cancelled",
            auditEventRef: $"phase8_audit_{actionId}_cancelled",
            cancellationToken);

        if (!updated.Success || updated.Record is null)
        {
            return Phase80PendingActionResult.Fail(
                updated.Status,
                updated.Message ?? "Pending action could not be cancelled.",
                updated.Record is null ? null : ToView(updated.Record));
        }

        return Phase80PendingActionResult.Ok(
            Cancelled,
            "已取消；没有写入 Firestore，也没有执行真实 tool。",
            ToView(updated.Record));
    }

    internal void SeedForTests(Phase80PendingActionRecord record)
    {
        var create = _store.CreateAsync(new PendingActionCreateRequest(
            PendingActionId: record.ActionId,
            PreviewId: $"preview_{record.ActionId}",
            ToolId: "phase8_preview_tool",
            ToolVersion: "1.0",
            AdapterId: "phase8_preview_adapter",
            ActionType: record.ActionType,
            UserSubjectRef: record.UserId,
            SessionSubjectRef: "agent_preview_default_session",
            RiskLevel: "low_preview_only",
            ExpiresAt: record.ExpiresAt,
            IdempotencyKeyHash: $"phase8_idem_{record.ActionId}",
            InputHash: $"phase8_input_{record.ActionId}",
            PreviewHash: $"phase8_preview_{record.ActionId}",
            PolicySnapshotRef: "phase8_preview_policy_default",
            TraceId: $"phase8_trace_{record.ActionId}",
            AuditEventRefs: new[] { $"phase8_audit_{record.ActionId}_seeded" },
            SanitizedPreviewRef: $"phase8_sanitized_preview_{record.ActionId}",
            ServerOnlyPayloadRef: $"phase8_payload_ref_{record.ActionId}",
            Payload: new Dictionary<string, string>
            {
                ["title"] = record.Title,
                ["summary"] = record.Summary
            },
            ValidationSnapshot: new Dictionary<string, string>
            {
                ["guardDecision"] = record.GuardDecision
            })).GetAwaiter().GetResult();

        if (create.Success && record.Status != Pending)
        {
            _ = _store.UpdateStatusAsync(new PendingActionStatusUpdate(
                record.ActionId,
                record.UserId,
                PendingActionStatus.ConfirmationRequired,
                ToPendingActionStatus(record.Status))).GetAwaiter().GetResult();
        }
    }

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
            Plan: ResolveStoredConfirmPlan(record));
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
            Reason = ReadMetadata(record, "routeReason", fallback.Reason)
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
            LifeRecordPreview => "已确认生活记录；默认未写入 life_events，也未执行真实操作；启用写入门禁时会写入 life_events。",
            ReminderPreview => "已确认提醒；当前仍未写入 reminders，也未执行真实操作。",
            PlanPreview => "已确认计划；当前仍未写入计划数据，也未执行真实操作。",
            _ => "已确认，但未执行；没有写入数据，也没有执行真实操作。"
        };
    }

    internal static Phase80ConfirmExecutionPlan ResolveConfirmExecutionPlan(string actionType)
    {
        return ResolveConfirmExecutionPlan(actionType, Phase80ConfirmWritePolicy.DefaultPreviewOnly());
    }

    internal static Phase80ConfirmExecutionPlan ResolveConfirmExecutionPlan(
        string actionType,
        Phase80ConfirmWritePolicy policy)
    {
        return actionType switch
        {
            LifeRecordPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetLifeEvents,
                WriteEnabled: policy.AllowLifeEventWrites,
                MemoryCandidateOnly: true,
                Reason: policy.AllowLifeEventWrites
                    ? "life_record_confirm_write_allowed_by_policy"
                    : "life_record_confirm_write_disabled_until_beta_gate"),
            ReminderPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetReminders,
                WriteEnabled: policy.AllowReminderWrites,
                MemoryCandidateOnly: true,
                Reason: policy.AllowReminderWrites
                    ? "reminder_confirm_write_allowed_by_policy"
                    : "reminder_confirm_write_disabled_until_beta_gate"),
            PlanPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "plan_confirm_preview_only_until_planning_store_exists"),
            _ => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "unknown_action_type_preview_only")
        };
    }

    internal static Phase80ConfirmWriteDecision ResolveConfirmWriteDecision(Phase80ConfirmExecutionPlan plan)
    {
        return ResolveConfirmWriteDecision(plan, Phase80NoOpConfirmWriteExecutor.Instance);
    }

    internal static Phase80ConfirmWriteDecision ResolveConfirmWriteDecision(
        Phase80ConfirmExecutionPlan plan,
        IPhase80ConfirmWriteExecutor executor)
    {
        if (!plan.WriteEnabled)
        {
            return new Phase80ConfirmWriteDecision(
                ExecutionReady: false,
                RealPathReady: false,
                ExecutorId: "none",
                Reason: "confirm_write_disabled_by_policy");
        }

        var readiness = executor.GetReadiness(plan);
        return new Phase80ConfirmWriteDecision(
            ExecutionReady: readiness.ExecutionReady,
            RealPathReady: readiness.RealPathReady,
            ExecutorId: readiness.ExecutorId,
            Reason: readiness.Reason);
    }

    internal static Phase80ConfirmWriteInvocationGate ResolveConfirmWriteInvocationGate(
        Phase80ConfirmExecutionPlan plan,
        Phase80ConfirmWriteDecision decision)
    {
        if (!plan.WriteEnabled)
        {
            return new Phase80ConfirmWriteInvocationGate(
                ShouldInvoke: false,
                Reason: "confirm_write_policy_disabled");
        }

        if (!decision.ExecutionReady || !decision.RealPathReady)
        {
            return new Phase80ConfirmWriteInvocationGate(
                ShouldInvoke: false,
                Reason: "confirm_write_executor_not_ready");
        }

        return new Phase80ConfirmWriteInvocationGate(
            ShouldInvoke: true,
            Reason: "confirm_write_all_gates_ready");
    }

    internal async Task<Phase80ConfirmWriteExecutionResult> TryExecuteConfirmWriteAsync(
        Phase80ConfirmExecutionRequest request,
        Phase80ConfirmWriteDecision decision,
        CancellationToken cancellationToken = default)
    {
        var gate = ResolveConfirmWriteInvocationGate(request.Plan, decision);
        if (!gate.ShouldInvoke)
        {
            return new Phase80ConfirmWriteExecutionResult(
                Success: false,
                Status: "skipped",
                Target: request.Plan.Target,
                ResourcePath: null,
                WroteData: false,
                RealWritePath: false,
                ExecutorId: decision.ExecutorId,
                Reason: gate.Reason);
        }

        return await _confirmWriteExecutor.ExecuteAsync(request, cancellationToken);
    }

    internal static Phase80MemoryPlan ResolveMemoryPlan(string actionType)
    {
        return actionType switch
        {
            LifeRecordPreview or ReminderPreview or PlanPreview => new Phase80MemoryPlan(
                Target: MemoryCandidateTarget,
                WriteEnabled: false,
                RequiresDedupe: true,
                RequiresMerge: true,
                RequiresConfirmation: true,
                Reason: "memory_candidate_only_until_dedupe_merge_confirm"),
            _ => new Phase80MemoryPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                RequiresDedupe: true,
                RequiresMerge: true,
                RequiresConfirmation: true,
                Reason: "unknown_action_type_no_memory_write")
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

internal static class Phase80PersonalHomeIntentRouter
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

    public static Phase80PersonalHomeIntentRoute Route(string title, string summary, string? requestedActionType)
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
                : "requested_action_type");
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

internal sealed record Phase80PersonalHomeIntentRoute(
    string Intent,
    string ActionType,
    string Disposition,
    string RiskLevel,
    bool RequiresPendingAction,
    string Reason);

public sealed record Phase80CreatePendingActionRequest(string? Title, string? Summary, string? ActionType = null);

public sealed record Phase80PendingActionRecord(
    string ActionId,
    string UserId,
    string Status,
    string Title,
    string Summary,
    string ActionType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    bool Executed,
    bool WroteData,
    bool ExecutionReady,
    string GuardDecision);

public sealed record Phase80PendingActionView(
    string ActionId,
    string Status,
    string Title,
    string Summary,
    string ActionType,
    string Intent,
    string Disposition,
    string RiskLevel,
    bool RequiresPendingAction,
    string RouteReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    bool Executed,
    bool WroteData,
    bool ExecutionReady,
    string GuardDecision,
    string SafetyMode,
    bool LegacyConfirmEndpointUsed,
    bool RealWritePath,
    bool IsArchived,
    string ConfirmTarget,
    bool ConfirmWriteEnabled,
    bool ConfirmWriteExecutionReady,
    bool ConfirmWriteRealPathReady,
    string ConfirmWriteExecutorId,
    string ConfirmWriteDecisionReason,
    bool MemoryCandidateOnly,
    string ConfirmPlanReason,
    string MemoryTarget,
    bool MemoryWriteEnabled,
    bool MemoryRequiresDedupe,
    bool MemoryRequiresMerge,
    bool MemoryRequiresConfirmation,
    string Message);

public sealed record Phase80ConfirmExecutionPlan(
    string Target,
    bool WriteEnabled,
    bool MemoryCandidateOnly,
    string Reason);

public sealed record Phase80ConfirmWriteDecision(
    bool ExecutionReady,
    bool RealPathReady,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWriteInvocationGate(
    bool ShouldInvoke,
    string Reason);

public interface IPhase80ConfirmWriteExecutor
{
    Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan);

    Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class Phase80NoOpConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    public static Phase80NoOpConfirmWriteExecutor Instance { get; } = new();

    private Phase80NoOpConfirmWriteExecutor()
    {
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: false,
            RealPathReady: false,
            ExecutorId: "noop_confirm_write_executor",
            Reason: "confirm_write_policy_enabled_but_executor_not_connected");
    }

    public Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Phase80ConfirmWriteExecutionResult(
            Success: false,
            Status: "skipped",
            Target: request.Plan.Target,
            ResourcePath: null,
            WroteData: false,
            RealWritePath: false,
            ExecutorId: "noop_confirm_write_executor",
            Reason: "noop_executor_never_writes"));
    }
}

public sealed class Phase80LifeEventConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    private readonly IAgentLifeEventWriter _writer;

    public Phase80LifeEventConfirmWriteExecutor(IAgentLifeEventWriter writer)
    {
        _writer = writer;
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        var ready = string.Equals(plan.Target, Phase80PendingActionRuntime.ConfirmTargetLifeEvents, StringComparison.Ordinal);
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: ready,
            RealPathReady: ready,
            ExecutorId: "phase80_life_event_confirm_write_executor",
            Reason: ready
                ? "life_event_confirm_write_executor_ready"
                : "executor_only_supports_life_events");
    }

    public async Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Plan.Target, Phase80PendingActionRuntime.ConfirmTargetLifeEvents, StringComparison.Ordinal) ||
            !string.Equals(request.ActionType, Phase80PendingActionRuntime.LifeRecordPreview, StringComparison.Ordinal))
        {
            return new Phase80ConfirmWriteExecutionResult(
                Success: false,
                Status: "skipped",
                Target: request.Plan.Target,
                ResourcePath: null,
                WroteData: false,
                RealWritePath: false,
                ExecutorId: "phase80_life_event_confirm_write_executor",
                Reason: "unsupported_confirm_write_target");
        }

        var createRequest = new CreateLifeEventRequest
        {
            Type = "life",
            Title = request.Title,
            Content = request.Summary
        };
        var lifeEvent = AgentLifeEventFactory.Create(request.UserId, request.ActionId, createRequest);
        await _writer.WriteAsync(request.UserId, lifeEvent.Id, lifeEvent, cancellationToken);

        return new Phase80ConfirmWriteExecutionResult(
            Success: true,
            Status: "written",
            Target: request.Plan.Target,
            ResourcePath: $"users/{request.UserId}/life_events/{lifeEvent.Id}",
            WroteData: true,
            RealWritePath: true,
            ExecutorId: "phase80_life_event_confirm_write_executor",
            Reason: "life_event_written_after_confirm");
    }
}

public sealed record Phase80ConfirmExecutionRequest(
    string UserId,
    string ActionId,
    string ActionType,
    string Title,
    string Summary,
    Phase80ConfirmExecutionPlan Plan);

public sealed record Phase80ConfirmWriteExecutionResult(
    bool Success,
    string Status,
    string Target,
    string? ResourcePath,
    bool WroteData,
    bool RealWritePath,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWriteExecutorReadiness(
    bool ExecutionReady,
    bool RealPathReady,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWritePolicy(
    bool AllowLifeEventWrites,
    bool AllowReminderWrites)
{
    public static Phase80ConfirmWritePolicy DefaultPreviewOnly()
    {
        return new Phase80ConfirmWritePolicy(
            AllowLifeEventWrites: false,
            AllowReminderWrites: false);
    }
}

public sealed record Phase80MemoryPlan(
    string Target,
    bool WriteEnabled,
    bool RequiresDedupe,
    bool RequiresMerge,
    bool RequiresConfirmation,
    string Reason);

public sealed record Phase80PendingActionResult(
    bool Success,
    string Status,
    string Message,
    Phase80PendingActionView? Data)
{
    public static Phase80PendingActionResult Ok(string status, string message, Phase80PendingActionView data)
    {
        return new Phase80PendingActionResult(true, status, message, data);
    }

    public static Phase80PendingActionResult Fail(string status, string message, Phase80PendingActionView? data = null)
    {
        return new Phase80PendingActionResult(false, status, message, data);
    }
}
