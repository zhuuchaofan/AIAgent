using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.UnifiedInbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Api.Services.Agent.Phase8;

// Name retained for compatibility with Phase 8/9 tests and docs. This is now
// the LifeOS Personal Home pending action runtime mainline.
public sealed partial class Phase80PendingActionRuntime
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
    private readonly IUnifiedInboxIntentClassifier _intentClassifier;
    private readonly ILogger<Phase80PendingActionRuntime> _logger;
    private readonly bool _enableConfirmWriteExecution;

    public Phase80PendingActionRuntime(
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        IPendingActionStore? store = null,
        string? safetyMode = null,
        Phase80ConfirmWritePolicy? confirmWritePolicy = null,
        IPhase80ConfirmWriteExecutor? confirmWriteExecutor = null,
        IUnifiedInboxIntentClassifier? intentClassifier = null,
        ILogger<Phase80PendingActionRuntime>? logger = null,
        bool enableConfirmWriteExecution = false)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _store = store ?? new InMemoryPendingActionStore(_timeProvider);
        _safetyMode = string.IsNullOrWhiteSpace(safetyMode) ? SafetyMode : safetyMode;
        _confirmWritePolicy = confirmWritePolicy ?? Phase80ConfirmWritePolicy.DefaultPreviewOnly();
        _confirmWriteExecutor = confirmWriteExecutor ?? Phase80NoOpConfirmWriteExecutor.Instance;
        _intentClassifier = intentClassifier ?? RuleBasedUnifiedInboxIntentClassifier.Instance;
        _logger = logger ?? NullLogger<Phase80PendingActionRuntime>.Instance;
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
        var route = await _intentClassifier.ClassifyAsync(
            new UnifiedInboxIntentClassifierRequest(title, summary, request?.ActionType, request?.ClientTimeZone),
            cancellationToken);
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
                ["intentClassifier"] = route.Classifier,
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
                ["routeReason"] = route.Reason,
                ["intentClassifier"] = route.Classifier
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

        _logger.LogInformation(
            "LifeOS pending action created. UserSubjectRef={UserSubjectRef} ActionId={ActionId} ActionType={ActionType} Intent={Intent} Disposition={Disposition} ConfirmTarget={ConfirmTarget} MemoryTarget={MemoryTarget} TraceId={TraceId}",
            userId,
            created.Record.PendingActionId,
            created.Record.ActionType,
            route.Intent,
            route.Disposition,
            confirmPlan.Target,
            memoryPlan.Target,
            created.Record.TraceId);

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
            var restoredView = ToView(current);
            return Phase80PendingActionResult.Ok(
                Confirmed,
                restoredView.WroteData
                    ? "该生活记录已经确认并写入 life_events。"
                    : "该动作已经确认，但仍未执行。",
                restoredView);
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
        _logger.LogInformation(
            "LifeOS pending action confirmed. UserSubjectRef={UserSubjectRef} ActionId={ActionId} ActionType={ActionType} Status={Status} WroteData={WroteData} Executed={Executed} ConfirmTarget={ConfirmTarget} Reason={Reason}",
            userId,
            actionId,
            confirmed.Record.ActionType,
            confirmed.Record.Status,
            confirmed.Record.WroteData,
            confirmed.Record.Executed,
            ResolveStoredConfirmPlan(confirmed.Record).Target,
            executionResult?.Reason ?? "confirm_write_not_executed");

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

}
