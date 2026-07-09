using System.Collections.Concurrent;

namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed class Phase80PendingActionRuntime
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string SafetyMode = "phase8_fake_first_in_memory";
    public const string GuardDecision = "deny_all_no_real_execution";

    private readonly ConcurrentDictionary<string, Phase80PendingActionRecord> _actions = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public Phase80PendingActionRuntime(TimeProvider? timeProvider = null, TimeSpan? ttl = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
    }

    public Phase80PendingActionResult Create(string userId, Phase80CreatePendingActionRequest? request)
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

        var record = new Phase80PendingActionRecord(
            ActionId: $"phase8_action_{Guid.NewGuid():N}",
            UserId: userId,
            Status: Pending,
            Title: title,
            Summary: summary,
            ActionType: "phase8_fake_pending_action",
            CreatedAt: now,
            UpdatedAt: now,
            ExpiresAt: now.Add(_ttl),
            ConfirmedAt: null,
            CancelledAt: null,
            Executed: false,
            WroteData: false,
            ExecutionReady: false,
            GuardDecision: GuardDecision);

        _actions[record.ActionId] = record;

        return Phase80PendingActionResult.Ok(
            "pending",
            "已生成待确认动作。该动作仅保存在本进程内存中。",
            ToView(record));
    }

    public IReadOnlyList<Phase80PendingActionView> List(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<Phase80PendingActionView>();
        }

        var now = _timeProvider.GetUtcNow();
        return _actions.Values
            .Where(action => string.Equals(action.UserId, userId, StringComparison.Ordinal))
            .Select(action => ToView(ExpireIfNeeded(action, now)))
            .OrderByDescending(action => action.CreatedAt)
            .ToArray();
    }

    public Phase80PendingActionResult Confirm(string userId, string actionId)
    {
        if (!TryGetOwnedAction(userId, actionId, out var current))
        {
            return Phase80PendingActionResult.Fail("not_found", "Pending action was not found.");
        }

        current = ExpireIfNeeded(current, _timeProvider.GetUtcNow());
        if (current.Status == Expired)
        {
            return Phase80PendingActionResult.Fail(Expired, "Pending action expired.", ToView(current));
        }

        if (current.Status == Cancelled)
        {
            return Phase80PendingActionResult.Fail(Cancelled, "Cancelled pending action cannot be confirmed.", ToView(current));
        }

        if (current.Status == Confirmed)
        {
            return Phase80PendingActionResult.Ok(
                Confirmed,
                "该动作已经确认，但仍未执行。",
                ToView(current));
        }

        var now = _timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = Confirmed,
            UpdatedAt = now,
            ConfirmedAt = now,
            Executed = false,
            WroteData = false,
            ExecutionReady = false,
            GuardDecision = GuardDecision
        };

        _actions[actionId] = updated;

        return Phase80PendingActionResult.Ok(
            Confirmed,
            "已确认，但未执行；没有写入 Firestore，也没有执行真实 tool。",
            ToView(updated));
    }

    public Phase80PendingActionResult Cancel(string userId, string actionId)
    {
        if (!TryGetOwnedAction(userId, actionId, out var current))
        {
            return Phase80PendingActionResult.Fail("not_found", "Pending action was not found.");
        }

        current = ExpireIfNeeded(current, _timeProvider.GetUtcNow());
        if (current.Status == Expired)
        {
            return Phase80PendingActionResult.Fail(Expired, "Pending action expired.", ToView(current));
        }

        if (current.Status == Confirmed)
        {
            return Phase80PendingActionResult.Fail(Confirmed, "Confirmed pending action cannot be cancelled in Phase 8.0.", ToView(current));
        }

        if (current.Status == Cancelled)
        {
            return Phase80PendingActionResult.Ok(
                Cancelled,
                "该动作已经取消。",
                ToView(current));
        }

        var now = _timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = Cancelled,
            UpdatedAt = now,
            CancelledAt = now,
            Executed = false,
            WroteData = false,
            ExecutionReady = false,
            GuardDecision = GuardDecision
        };

        _actions[actionId] = updated;

        return Phase80PendingActionResult.Ok(
            Cancelled,
            "已取消；没有写入 Firestore，也没有执行真实 tool。",
            ToView(updated));
    }

    internal void SeedForTests(Phase80PendingActionRecord record)
    {
        _actions[record.ActionId] = record;
    }

    private bool TryGetOwnedAction(string userId, string actionId, out Phase80PendingActionRecord record)
    {
        record = default!;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        if (!_actions.TryGetValue(actionId, out var current))
        {
            return false;
        }

        if (!string.Equals(current.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        record = current;
        return true;
    }

    private Phase80PendingActionRecord ExpireIfNeeded(Phase80PendingActionRecord current, DateTimeOffset now)
    {
        if (current.Status != Pending || current.ExpiresAt > now)
        {
            return current;
        }

        var expired = current with
        {
            Status = Expired,
            UpdatedAt = now,
            Executed = false,
            WroteData = false,
            ExecutionReady = false,
            GuardDecision = GuardDecision
        };
        _actions[current.ActionId] = expired;
        return expired;
    }

    private static Phase80PendingActionView ToView(Phase80PendingActionRecord record)
    {
        return new Phase80PendingActionView(
            ActionId: record.ActionId,
            Status: record.Status,
            Title: record.Title,
            Summary: record.Summary,
            ActionType: record.ActionType,
            CreatedAt: record.CreatedAt,
            UpdatedAt: record.UpdatedAt,
            ExpiresAt: record.ExpiresAt,
            ConfirmedAt: record.ConfirmedAt,
            CancelledAt: record.CancelledAt,
            Executed: record.Executed,
            WroteData: record.WroteData,
            ExecutionReady: record.ExecutionReady,
            GuardDecision: record.GuardDecision,
            SafetyMode: SafetyMode,
            LegacyConfirmEndpointUsed: false,
            RealWritePath: false,
            Message: record.Status switch
            {
                Confirmed => "已确认，但未执行",
                Cancelled => "已取消",
                Expired => "已过期，不能确认",
                _ => "待确认"
            });
    }
}

public sealed record Phase80CreatePendingActionRequest(string? Title, string? Summary);

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
    string Message);

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
