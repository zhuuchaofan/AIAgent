using Google.Cloud.Firestore;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed partial class FirestorePendingActionStore : IPendingActionStore
{
    public const string CollectionName = "pendingActions";

    private readonly FirestoreDb _db;
    private readonly TimeProvider _timeProvider;

    public FirestorePendingActionStore(FirestoreDb db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PendingActionStoreResult> CreateAsync(
        PendingActionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = PendingActionCreateRequestValidator.Validate(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var existing = await CheckIdempotencyKeyHashAsync(
            request.UserSubjectRef,
            request.IdempotencyKeyHash,
            cancellationToken);
        if (existing.Success && existing.Record is not null)
        {
            return PendingActionStoreResult.Succeeded(existing.Record, idempotent: true);
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
            Executed = false,
            IsArchived = false
        };

        var document = Document(request.UserSubjectRef, request.PendingActionId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(document, cancellationToken);
            if (snapshot.Exists)
            {
                var storedRecord = FromDocument(snapshot);
                if (string.Equals(storedRecord.UserSubjectRef, request.UserSubjectRef, StringComparison.Ordinal) &&
                    string.Equals(storedRecord.IdempotencyKeyHash, request.IdempotencyKeyHash, StringComparison.Ordinal))
                {
                    return PendingActionStoreResult.Succeeded(storedRecord, idempotent: true);
                }

                return PendingActionStoreResult.Failed(
                    "duplicate",
                    "duplicate_pending_action",
                    "Pending action id already exists.");
            }

            transaction.Set(document, ToDocument(record));
            return PendingActionStoreResult.Succeeded(record);
        }, cancellationToken: cancellationToken);
    }

    public async Task<PendingActionRecord?> GetByIdAsync(
        string userSubjectRef,
        string pendingActionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await Document(userSubjectRef, pendingActionId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return null;
        }

        var record = FromDocument(snapshot);
        if (record.UserSubjectRef != userSubjectRef)
        {
            return null;
        }

        return await ExpireIfNeededAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingActionClientView>> GetActiveByUserAsync(
        string userSubjectRef,
        CancellationToken cancellationToken = default)
    {
        var records = await QueryAsync(new PendingActionQuery(userSubjectRef, ActiveOnly: true), cancellationToken);
        return records.Select(record => record.ToClientView()).ToArray();
    }

    public async Task<PendingActionStoreResult> UpdateStatusAsync(
        PendingActionStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        return await MutateOwnedRecordInTransactionAsync(
            update.UserSubjectRef,
            update.PendingActionId,
            record => PendingActionTransitionPolicy.ValidateStatusUpdate(record, update),
            record => Copy(record, update.NewStatus) with
            {
                ConfirmationId = update.ConfirmationId ?? record.ConfirmationId,
                BlockedReason = update.BlockedReason,
                CancellationReason = update.CancellationReason,
                WroteData = update.WroteData ?? record.WroteData,
                Executed = update.Executed ?? record.Executed,
                AuditEventRefs = AppendAudit(record, update.AuditEventRef)
            },
            cancellationToken);
    }

    public Task<PendingActionStoreResult> MarkExpiredAsync(
        string userSubjectRef,
        string pendingActionId,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateOwnedStatusAsync(
            userSubjectRef,
            pendingActionId,
            PendingActionStatus.Expired,
            auditEventRef: auditEventRef,
            cancellationToken: cancellationToken);
    }

    public Task<PendingActionStoreResult> CancelAsync(
        string userSubjectRef,
        string pendingActionId,
        string? cancellationReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateOwnedStatusAsync(
            userSubjectRef,
            pendingActionId,
            PendingActionStatus.Cancelled,
            cancellationReason,
            auditEventRef,
            cancellationToken);
    }

    public async Task<PendingActionStoreResult> RecordConfirmationReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string confirmationId,
        string confirmationRequestHash,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return await MutateOwnedRecordInTransactionAsync(
            userSubjectRef,
            pendingActionId,
            record => PendingActionTransitionPolicy.ValidateMutableMetadataUpdate(record),
            record => Copy(record, record.Status) with
            {
                ConfirmationId = confirmationId,
                ValidationSnapshot = Merge(record.ValidationSnapshot, "confirmationRequestHash", confirmationRequestHash),
                AuditEventRefs = AppendAudit(record, auditEventRef)
            },
            cancellationToken);
    }

    public async Task<PendingActionStoreResult> RecordGuardDecisionReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string guardDecisionRef,
        string status,
        string? blockedReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return await MutateOwnedRecordInTransactionAsync(
            userSubjectRef,
            pendingActionId,
            record => PendingActionTransitionPolicy.ValidateOwnedStatusChange(record, status),
            record => Copy(record, status) with
            {
                ValidationSnapshot = Merge(record.ValidationSnapshot, "guardDecisionRef", guardDecisionRef),
                BlockedReason = blockedReason,
                AuditEventRefs = AppendAudit(record, auditEventRef)
            },
            cancellationToken);
    }

    public async Task<PendingActionStoreResult> CheckIdempotencyKeyHashAsync(
        string userSubjectRef,
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default)
    {
        var records = await QueryAsync(
            new PendingActionQuery(userSubjectRef, IdempotencyKeyHash: idempotencyKeyHash),
            cancellationToken);
        var existing = records.FirstOrDefault();
        return existing is null
            ? PendingActionStoreResult.Failed("not_found", "not_found", "Idempotency key was not found.")
            : PendingActionStoreResult.Succeeded(existing, idempotent: true);
    }

    public async Task<PendingActionRecord?> GetByPreviewIdAsync(
        string userSubjectRef,
        string previewId,
        CancellationToken cancellationToken = default)
    {
        var records = await QueryAsync(new PendingActionQuery(userSubjectRef, PreviewId: previewId), cancellationToken);
        return records.FirstOrDefault();
    }

    public async Task<PendingActionRecord?> GetByConfirmationIdAsync(
        string userSubjectRef,
        string confirmationId,
        CancellationToken cancellationToken = default)
    {
        var records = await QueryAsync(new PendingActionQuery(userSubjectRef, ConfirmationId: confirmationId), cancellationToken);
        return records.FirstOrDefault();
    }

    public async Task<IReadOnlyList<PendingActionRecord>> QueryAsync(
        PendingActionQuery query,
        CancellationToken cancellationToken = default)
    {
        Query firestoreQuery = UserCollection(query.UserSubjectRef);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            firestoreQuery = firestoreQuery.WhereEqualTo("status", query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.PreviewId))
        {
            firestoreQuery = firestoreQuery.WhereEqualTo("previewId", query.PreviewId);
        }
        if (!string.IsNullOrWhiteSpace(query.ConfirmationId))
        {
            firestoreQuery = firestoreQuery.WhereEqualTo("confirmationId", query.ConfirmationId);
        }
        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            firestoreQuery = firestoreQuery.WhereEqualTo("traceId", query.TraceId);
        }
        if (!string.IsNullOrWhiteSpace(query.IdempotencyKeyHash))
        {
            firestoreQuery = firestoreQuery.WhereEqualTo("idempotencyKeyHash", query.IdempotencyKeyHash);
        }
        var snapshot = await firestoreQuery.GetSnapshotAsync(cancellationToken);
        var records = new List<PendingActionRecord>();
        foreach (var document in snapshot.Documents)
        {
            var record = FromDocument(document);
            if (record.UserSubjectRef != query.UserSubjectRef)
            {
                continue;
            }

            record = await ExpireIfNeededAsync(record, cancellationToken);
            if (query.ActiveOnly && !PendingActionStatus.IsActive(record.Status))
            {
                continue;
            }
            if (!query.IncludeArchived && record.IsArchived)
            {
                continue;
            }

            records.Add(record);
        }

        return records.OrderByDescending(record => record.CreatedAt).ToArray();
    }

    public async Task<PendingActionStoreResult> ArchiveAsync(
        string userSubjectRef,
        string pendingActionId,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return await MutateOwnedRecordInTransactionAsync(
            userSubjectRef,
            pendingActionId,
            _ => null,
            record => Copy(record, record.Status) with
            {
                IsArchived = true,
                ArchivedAt = _timeProvider.GetUtcNow(),
                ArchivedByUserId = userSubjectRef,
                AuditEventRefs = AppendAudit(record, auditEventRef)
            },
            cancellationToken);
    }

}
