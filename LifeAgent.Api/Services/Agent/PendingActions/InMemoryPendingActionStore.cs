using System.Collections.Concurrent;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed class InMemoryPendingActionStore : IPendingActionStore
{
    private readonly ConcurrentDictionary<string, PendingActionRecord> _records = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryPendingActionStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PendingActionStoreResult> CreateAsync(
        PendingActionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserSubjectRef) ||
            string.IsNullOrWhiteSpace(request.PendingActionId))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "invalid_request",
                "invalid_request",
                "Pending action requires an authenticated owner and id."));
        }

        var existing = _records.Values.FirstOrDefault(record =>
            record.UserSubjectRef == request.UserSubjectRef &&
            record.IdempotencyKeyHash == request.IdempotencyKeyHash);
        if (existing is not null)
        {
            return Task.FromResult(PendingActionStoreResult.Succeeded(existing, idempotent: true));
        }

        var now = _timeProvider.GetUtcNow();
        var status = request.ExpiresAt <= now
            ? PendingActionStatus.Expired
            : PendingActionStatus.ConfirmationRequired;
        var record = new PendingActionRecord
        {
            PendingActionId = request.PendingActionId,
            PreviewId = request.PreviewId,
            ToolId = request.ToolId,
            ToolVersion = request.ToolVersion,
            AdapterId = request.AdapterId,
            ActionType = request.ActionType,
            UserSubjectRef = request.UserSubjectRef,
            SessionSubjectRef = request.SessionSubjectRef,
            RiskLevel = request.RiskLevel,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = request.ExpiresAt,
            IdempotencyKeyHash = request.IdempotencyKeyHash,
            InputHash = request.InputHash,
            PreviewHash = request.PreviewHash,
            PolicySnapshotRef = request.PolicySnapshotRef,
            TraceId = request.TraceId,
            AuditEventRefs = request.AuditEventRefs,
            SanitizedPreviewRef = request.SanitizedPreviewRef,
            ServerOnlyPayloadRef = request.ServerOnlyPayloadRef,
            Payload = request.Payload ?? new Dictionary<string, string>(),
            RedactionMetadata = request.RedactionMetadata ?? new Dictionary<string, string>(),
            ValidationSnapshot = request.ValidationSnapshot ?? new Dictionary<string, string>(),
            WroteData = false,
            Executed = false
        };

        if (!_records.TryAdd(record.PendingActionId, record))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "duplicate",
                "duplicate_pending_action",
                "Pending action id already exists."));
        }

        return Task.FromResult(PendingActionStoreResult.Succeeded(record));
    }

    public Task<PendingActionRecord?> GetByIdAsync(
        string userSubjectRef,
        string pendingActionId,
        CancellationToken cancellationToken = default)
    {
        ExpireDueRecords();
        return Task.FromResult(TryGetOwned(userSubjectRef, pendingActionId, out var record) ? record : null);
    }

    public Task<IReadOnlyList<PendingActionClientView>> GetActiveByUserAsync(
        string userSubjectRef,
        CancellationToken cancellationToken = default)
    {
        ExpireDueRecords();
        var results = _records.Values
            .Where(record => record.UserSubjectRef == userSubjectRef)
            .Where(record => PendingActionStatus.IsActive(record.Status))
            .Select(record => record.ToClientView())
            .OrderByDescending(record => record.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<PendingActionClientView>>(results);
    }

    public Task<PendingActionStoreResult> UpdateStatusAsync(
        PendingActionStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        ExpireDueRecords();
        if (!TryGetOwned(update.UserSubjectRef, update.PendingActionId, out var record))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "not_found",
                "not_found",
                "Pending action was not found."));
        }

        var transitionError = PendingActionTransitionPolicy.ValidateStatusUpdate(record, update);
        if (transitionError is not null)
        {
            return Task.FromResult(transitionError);
        }

        var updated = Copy(record, update.NewStatus) with
        {
            ConfirmationId = update.ConfirmationId ?? record.ConfirmationId,
            BlockedReason = update.BlockedReason,
            CancellationReason = update.CancellationReason,
            AuditEventRefs = AppendAudit(record, update.AuditEventRef)
        };
        _records[updated.PendingActionId] = updated;
        return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
    }

    public Task<PendingActionStoreResult> MarkExpiredAsync(
        string userSubjectRef,
        string pendingActionId,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateOwnedStatus(userSubjectRef, pendingActionId, PendingActionStatus.Expired, auditEventRef: auditEventRef);
    }

    public Task<PendingActionStoreResult> CancelAsync(
        string userSubjectRef,
        string pendingActionId,
        string? cancellationReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateOwnedStatus(userSubjectRef, pendingActionId, PendingActionStatus.Cancelled, cancellationReason, auditEventRef);
    }

    public Task<PendingActionStoreResult> RecordConfirmationReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string confirmationId,
        string confirmationRequestHash,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "not_found",
                "not_found",
                "Pending action was not found."));
        }

        var transitionError = PendingActionTransitionPolicy.ValidateMutableMetadataUpdate(record);
        if (transitionError is not null)
        {
            return Task.FromResult(transitionError);
        }

        var updated = Copy(record, record.Status) with
        {
            ConfirmationId = confirmationId,
            ValidationSnapshot = Merge(record.ValidationSnapshot, "confirmationRequestHash", confirmationRequestHash),
            AuditEventRefs = AppendAudit(record, auditEventRef)
        };
        _records[updated.PendingActionId] = updated;
        return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
    }

    public Task<PendingActionStoreResult> RecordGuardDecisionReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string guardDecisionRef,
        string status,
        string? blockedReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "not_found",
                "not_found",
                "Pending action was not found."));
        }

        var transitionError = PendingActionTransitionPolicy.ValidateOwnedStatusChange(record, status);
        if (transitionError is not null)
        {
            return Task.FromResult(transitionError);
        }

        var updated = Copy(record, status) with
        {
            ValidationSnapshot = Merge(record.ValidationSnapshot, "guardDecisionRef", guardDecisionRef),
            BlockedReason = blockedReason,
            AuditEventRefs = AppendAudit(record, auditEventRef)
        };
        _records[updated.PendingActionId] = updated;
        return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
    }

    public Task<PendingActionStoreResult> CheckIdempotencyKeyHashAsync(
        string userSubjectRef,
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default)
    {
        var existing = _records.Values.FirstOrDefault(record =>
            record.UserSubjectRef == userSubjectRef &&
            record.IdempotencyKeyHash == idempotencyKeyHash);
        return Task.FromResult(existing is null
            ? PendingActionStoreResult.Failed("not_found", "not_found", "Idempotency key was not found.")
            : PendingActionStoreResult.Succeeded(existing, idempotent: true));
    }

    public Task<PendingActionRecord?> GetByPreviewIdAsync(
        string userSubjectRef,
        string previewId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.Values.FirstOrDefault(record =>
            record.UserSubjectRef == userSubjectRef &&
            record.PreviewId == previewId));
    }

    public Task<PendingActionRecord?> GetByConfirmationIdAsync(
        string userSubjectRef,
        string confirmationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_records.Values.FirstOrDefault(record =>
            record.UserSubjectRef == userSubjectRef &&
            record.ConfirmationId == confirmationId));
    }

    public Task<IReadOnlyList<PendingActionRecord>> QueryAsync(
        PendingActionQuery query,
        CancellationToken cancellationToken = default)
    {
        ExpireDueRecords();
        var records = _records.Values.Where(record => record.UserSubjectRef == query.UserSubjectRef);
        if (query.ActiveOnly)
        {
            records = records.Where(record => PendingActionStatus.IsActive(record.Status));
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            records = records.Where(record => record.Status == query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.PreviewId))
        {
            records = records.Where(record => record.PreviewId == query.PreviewId);
        }
        if (!string.IsNullOrWhiteSpace(query.ConfirmationId))
        {
            records = records.Where(record => record.ConfirmationId == query.ConfirmationId);
        }
        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            records = records.Where(record => record.TraceId == query.TraceId);
        }
        if (!string.IsNullOrWhiteSpace(query.IdempotencyKeyHash))
        {
            records = records.Where(record => record.IdempotencyKeyHash == query.IdempotencyKeyHash);
        }

        return Task.FromResult<IReadOnlyList<PendingActionRecord>>(
            records.OrderByDescending(record => record.CreatedAt).ToList());
    }

    private Task<PendingActionStoreResult> UpdateOwnedStatus(
        string userSubjectRef,
        string pendingActionId,
        string status,
        string? cancellationReason = null,
        string? auditEventRef = null)
    {
        ExpireDueRecords();
        if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
        {
            return Task.FromResult(PendingActionStoreResult.Failed(
                "not_found",
                "not_found",
                "Pending action was not found."));
        }

        var transitionError = PendingActionTransitionPolicy.ValidateOwnedStatusChange(record, status);
        if (transitionError is not null)
        {
            return Task.FromResult(transitionError);
        }

        var updated = Copy(record, status) with
        {
            CancellationReason = cancellationReason,
            AuditEventRefs = AppendAudit(record, auditEventRef)
        };
        _records[updated.PendingActionId] = updated;
        return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
    }

    private bool TryGetOwned(string userSubjectRef, string pendingActionId, out PendingActionRecord record)
    {
        if (_records.TryGetValue(pendingActionId, out var found) &&
            found.UserSubjectRef == userSubjectRef)
        {
            record = found;
            return true;
        }

        record = null!;
        return false;
    }

    private void ExpireDueRecords()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var record in _records.Values.Where(record =>
                     PendingActionStatus.IsActive(record.Status) &&
                     record.ExpiresAt <= now))
        {
            _records[record.PendingActionId] = Copy(record, PendingActionStatus.Expired);
        }
    }

    private PendingActionRecord Copy(PendingActionRecord record, string status)
    {
        return new PendingActionRecord
        {
            PendingActionId = record.PendingActionId,
            PreviewId = record.PreviewId,
            ConfirmationId = record.ConfirmationId,
            ToolId = record.ToolId,
            ToolVersion = record.ToolVersion,
            AdapterId = record.AdapterId,
            ActionType = record.ActionType,
            UserSubjectRef = record.UserSubjectRef,
            SessionSubjectRef = record.SessionSubjectRef,
            RiskLevel = record.RiskLevel,
            Status = status,
            CreatedAt = record.CreatedAt,
            UpdatedAt = _timeProvider.GetUtcNow(),
            ExpiresAt = record.ExpiresAt,
            IdempotencyKeyHash = record.IdempotencyKeyHash,
            InputHash = record.InputHash,
            PreviewHash = record.PreviewHash,
            PolicySnapshotRef = record.PolicySnapshotRef,
            TraceId = record.TraceId,
            AuditEventRefs = record.AuditEventRefs,
            SanitizedPreviewRef = record.SanitizedPreviewRef,
            ServerOnlyPayloadRef = record.ServerOnlyPayloadRef,
            Payload = record.Payload,
            RedactionMetadata = record.RedactionMetadata,
            ValidationSnapshot = record.ValidationSnapshot,
            BlockedReason = record.BlockedReason,
            CancellationReason = record.CancellationReason,
            SchemaVersion = record.SchemaVersion,
            WroteData = false,
            Executed = false
        };
    }

    private static IReadOnlyList<string> AppendAudit(PendingActionRecord record, string? auditEventRef)
    {
        return string.IsNullOrWhiteSpace(auditEventRef)
            ? record.AuditEventRefs
            : record.AuditEventRefs.Concat(new[] { auditEventRef }).ToList();
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> values,
        string key,
        string value)
    {
        var merged = new Dictionary<string, string>(values)
        {
            [key] = value
        };
        return merged;
    }
}
