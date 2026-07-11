using LifeAgent.Api.Services.Agent.PendingActions;

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

    public Phase80PendingActionRuntime(
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        IPendingActionStore? store = null,
        string? safetyMode = null,
        Phase80ConfirmWritePolicy? confirmWritePolicy = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _store = store ?? new InMemoryPendingActionStore(_timeProvider);
        _safetyMode = string.IsNullOrWhiteSpace(safetyMode) ? SafetyMode : safetyMode;
        _confirmWritePolicy = confirmWritePolicy ?? Phase80ConfirmWritePolicy.DefaultPreviewOnly();
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
                ["requiresPendingAction"] = route.RequiresPendingAction.ToString()
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
        return records.Select(ToView).ToArray();
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

        var updated = await _store.UpdateStatusAsync(new PendingActionStatusUpdate(
            PendingActionId: actionId,
            UserSubjectRef: userId,
            ExpectedStatus: PendingActionStatus.ConfirmationRequired,
            NewStatus: PendingActionStatus.Confirmed,
            ConfirmationId: $"phase8_confirmation_{actionId}",
            AuditEventRef: $"phase8_audit_{actionId}_confirmed"),
            cancellationToken);

        if (!updated.Success || updated.Record is null)
        {
            return Phase80PendingActionResult.Fail(
                updated.Status,
                updated.Message ?? "Pending action could not be confirmed.",
                updated.Record is null ? null : ToView(updated.Record));
        }

        return Phase80PendingActionResult.Ok(
            Confirmed,
            ConfirmedPreviewMessage(updated.Record.ActionType),
            ToView(updated.Record));
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

    private Phase80PendingActionView ToView(PendingActionRecord record)
    {
        var status = ToPhase80Status(record.Status);
        var confirmPlan = ResolveConfirmExecutionPlan(record.ActionType, _confirmWritePolicy);
        var memoryPlan = ResolveMemoryPlan(record.ActionType);
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
            Executed: false,
            WroteData: false,
            ExecutionReady: false,
            GuardDecision: GuardDecision,
            SafetyMode: _safetyMode,
            LegacyConfirmEndpointUsed: false,
            RealWritePath: false,
            IsArchived: record.IsArchived,
            ConfirmTarget: confirmPlan.Target,
            ConfirmWriteEnabled: confirmPlan.WriteEnabled,
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
            LifeRecordPreview => "已确认生活记录；当前仍未写入 life_events，也未执行真实操作。",
            ReminderPreview => "已确认提醒；当前仍未写入 reminders，也未执行真实操作。",
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
            _ => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "unknown_action_type_preview_only")
        };
    }

    internal static Phase80MemoryPlan ResolveMemoryPlan(string actionType)
    {
        return actionType switch
        {
            LifeRecordPreview or ReminderPreview => new Phase80MemoryPlan(
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
        var intent = actionType == Phase80PendingActionRuntime.ReminderPreview ? ReminderIntent : LifeRecordIntent;
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
        if (string.Equals(actionType, Phase80PendingActionRuntime.LifeRecordPreview, StringComparison.OrdinalIgnoreCase))
        {
            return Phase80PendingActionRuntime.LifeRecordPreview;
        }

        if (string.Equals(actionType, Phase80PendingActionRuntime.ReminderPreview, StringComparison.OrdinalIgnoreCase))
        {
            return Phase80PendingActionRuntime.ReminderPreview;
        }

        return LooksLikeReminder(source)
            ? Phase80PendingActionRuntime.ReminderPreview
            : Phase80PendingActionRuntime.LifeRecordPreview;
    }

    private static bool LooksLikeReminder(string value)
    {
        return value.Contains("提醒", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("闹钟", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("到点", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("明天", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("后天", StringComparison.OrdinalIgnoreCase);
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
