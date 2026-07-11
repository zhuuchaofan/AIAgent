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

    private readonly IPendingActionStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly string _safetyMode;

    public Phase80PendingActionRuntime(
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        IPendingActionStore? store = null,
        string? safetyMode = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _store = store ?? new InMemoryPendingActionStore(_timeProvider);
        _safetyMode = string.IsNullOrWhiteSpace(safetyMode) ? SafetyMode : safetyMode;
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
        var actionType = NormalizeActionType(request?.ActionType, $"{title} {summary}");

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
            RiskLevel: "low_preview_only",
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
                ["actionType"] = actionType
            },
            RedactionMetadata: new Dictionary<string, string>
            {
                ["mode"] = "preview_only"
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
        var confirmPlan = ResolveConfirmExecutionPlan(record.ActionType);
        return new Phase80PendingActionView(
            ActionId: record.PendingActionId,
            Status: status,
            Title: ReadPayload(record, "title", "保存一条测试生活记录"),
            Summary: ReadPayload(record, "summary", "Phase 8 fake-first 待确认动作。"),
            ActionType: record.ActionType,
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

    private static string NormalizeActionType(string? actionType, string source)
    {
        if (string.Equals(actionType, LifeRecordPreview, StringComparison.OrdinalIgnoreCase))
        {
            return LifeRecordPreview;
        }

        if (string.Equals(actionType, ReminderPreview, StringComparison.OrdinalIgnoreCase))
        {
            return ReminderPreview;
        }

        return LooksLikeReminder(source) ? ReminderPreview : LifeRecordPreview;
    }

    private static bool LooksLikeReminder(string value)
    {
        return value.Contains("提醒", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("闹钟", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("到点", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("明天", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("后天", StringComparison.OrdinalIgnoreCase);
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
        return actionType switch
        {
            LifeRecordPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetLifeEvents,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "life_record_confirm_write_disabled_until_beta_gate"),
            ReminderPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetReminders,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "reminder_confirm_write_disabled_until_beta_gate"),
            _ => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "unknown_action_type_preview_only")
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
    string Message);

public sealed record Phase80ConfirmExecutionPlan(
    string Target,
    bool WriteEnabled,
    bool MemoryCandidateOnly,
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
