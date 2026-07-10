using Google.Cloud.Firestore;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed class FirestorePendingActionStore : IPendingActionStore
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
            Executed = false
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

            records.Add(record);
        }

        return records.OrderByDescending(record => record.CreatedAt).ToArray();
    }

    private async Task<PendingActionStoreResult> UpdateOwnedStatusAsync(
        string userSubjectRef,
        string pendingActionId,
        string status,
        string? cancellationReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return await MutateOwnedRecordInTransactionAsync(
            userSubjectRef,
            pendingActionId,
            record => PendingActionTransitionPolicy.ValidateOwnedStatusChange(record, status),
            record => Copy(record, status) with
            {
                CancellationReason = cancellationReason,
                AuditEventRefs = AppendAudit(record, auditEventRef)
            },
            cancellationToken);
    }

    private async Task<PendingActionRecord> ExpireIfNeededAsync(
        PendingActionRecord record,
        CancellationToken cancellationToken)
    {
        var (current, shouldPersist) = ExpireIfNeeded(record);
        if (!shouldPersist)
        {
            return current;
        }

        await MutateOwnedRecordInTransactionAsync(
            current.UserSubjectRef,
            current.PendingActionId,
            _ => null,
            storedRecord => ExpireIfNeeded(storedRecord).Record,
            cancellationToken);
        return current;
    }

    private async Task<PendingActionStoreResult> MutateOwnedRecordInTransactionAsync(
        string userSubjectRef,
        string pendingActionId,
        Func<PendingActionRecord, PendingActionStoreResult?> validate,
        Func<PendingActionRecord, PendingActionRecord> mutate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userSubjectRef) || string.IsNullOrWhiteSpace(pendingActionId))
        {
            return PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found.");
        }

        var document = Document(userSubjectRef, pendingActionId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(document, cancellationToken);
            if (!snapshot.Exists)
            {
                return PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found.");
            }

            var storedRecord = FromDocument(snapshot);
            if (!string.Equals(storedRecord.UserSubjectRef, userSubjectRef, StringComparison.Ordinal))
            {
                return PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found.");
            }

            var (record, expiredDuringTransaction) = ExpireIfNeeded(storedRecord);
            if (expiredDuringTransaction)
            {
                transaction.Set(document, ToDocument(record));
            }

            var transitionError = validate(record);
            if (transitionError is not null)
            {
                return transitionError;
            }

            var updated = mutate(record);
            transaction.Set(document, ToDocument(updated));
            return PendingActionStoreResult.Succeeded(updated);
        }, cancellationToken: cancellationToken);
    }

    private (PendingActionRecord Record, bool ShouldPersist) ExpireIfNeeded(PendingActionRecord record)
    {
        if (!PendingActionStatus.IsActive(record.Status) ||
            record.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return (record, false);
        }

        return (Copy(record, PendingActionStatus.Expired), true);
    }

    private CollectionReference UserCollection(string userSubjectRef)
    {
        return _db.Collection("users")
            .Document(userSubjectRef)
            .Collection(CollectionName);
    }

    private DocumentReference Document(string userSubjectRef, string pendingActionId)
    {
        return UserCollection(userSubjectRef).Document(pendingActionId);
    }

    internal static Dictionary<string, object?> ToDocument(PendingActionRecord record)
    {
        return new Dictionary<string, object?>
        {
            ["pendingActionId"] = record.PendingActionId,
            ["userId"] = record.UserSubjectRef,
            ["userSubjectRef"] = record.UserSubjectRef,
            ["previewId"] = record.PreviewId,
            ["confirmationId"] = record.ConfirmationId,
            ["toolId"] = record.ToolId,
            ["toolVersion"] = record.ToolVersion,
            ["adapterId"] = record.AdapterId,
            ["actionType"] = record.ActionType,
            ["sessionSubjectRef"] = record.SessionSubjectRef,
            ["riskLevel"] = record.RiskLevel,
            ["status"] = record.Status,
            ["payload"] = record.Payload,
            ["createdAt"] = ToTimestamp(record.CreatedAt),
            ["updatedAt"] = ToTimestamp(record.UpdatedAt),
            ["confirmedAt"] = record.Status == PendingActionStatus.Confirmed ? ToTimestamp(record.UpdatedAt) : null,
            ["cancelledAt"] = record.Status == PendingActionStatus.Cancelled ? ToTimestamp(record.UpdatedAt) : null,
            ["expiresAt"] = ToTimestamp(record.ExpiresAt),
            ["idempotencyKeyHash"] = record.IdempotencyKeyHash,
            ["inputHash"] = record.InputHash,
            ["previewHash"] = record.PreviewHash,
            ["policySnapshotRef"] = record.PolicySnapshotRef,
            ["traceId"] = record.TraceId,
            ["auditEventRefs"] = record.AuditEventRefs,
            ["audit"] = new Dictionary<string, object?>
            {
                ["createdByUserId"] = record.UserSubjectRef,
                ["updatedAt"] = ToTimestamp(record.UpdatedAt),
                ["refs"] = record.AuditEventRefs
            },
            ["sanitizedPreviewRef"] = record.SanitizedPreviewRef,
            ["serverOnlyPayloadRef"] = record.ServerOnlyPayloadRef,
            ["redactionMetadata"] = record.RedactionMetadata,
            ["validationSnapshot"] = record.ValidationSnapshot,
            ["blockedReason"] = record.BlockedReason,
            ["cancellationReason"] = record.CancellationReason,
            ["schemaVersion"] = record.SchemaVersion,
            ["wroteData"] = false,
            ["executed"] = false
        };
    }

    internal static PendingActionRecord FromDocument(DocumentSnapshot snapshot)
    {
        return FromDictionary(snapshot.ToDictionary());
    }

    internal static PendingActionRecord FromDictionary(IDictionary<string, object> data)
    {
        var userSubjectRef = ReadString(data, "userSubjectRef");
        if (string.IsNullOrWhiteSpace(userSubjectRef))
        {
            userSubjectRef = ReadString(data, "userId");
        }

        return new PendingActionRecord
        {
            PendingActionId = ReadString(data, "pendingActionId"),
            PreviewId = ReadString(data, "previewId"),
            ConfirmationId = ReadNullableString(data, "confirmationId"),
            ToolId = ReadString(data, "toolId"),
            ToolVersion = ReadString(data, "toolVersion"),
            AdapterId = ReadString(data, "adapterId"),
            ActionType = ReadString(data, "actionType"),
            UserSubjectRef = userSubjectRef,
            SessionSubjectRef = ReadString(data, "sessionSubjectRef"),
            RiskLevel = ReadString(data, "riskLevel"),
            Status = ReadString(data, "status"),
            CreatedAt = ReadDateTimeOffset(data, "createdAt"),
            UpdatedAt = ReadDateTimeOffset(data, "updatedAt"),
            ExpiresAt = ReadDateTimeOffset(data, "expiresAt"),
            IdempotencyKeyHash = ReadString(data, "idempotencyKeyHash"),
            InputHash = ReadString(data, "inputHash"),
            PreviewHash = ReadString(data, "previewHash"),
            PolicySnapshotRef = ReadString(data, "policySnapshotRef"),
            TraceId = ReadString(data, "traceId"),
            AuditEventRefs = ReadStringList(data, "auditEventRefs"),
            SanitizedPreviewRef = ReadString(data, "sanitizedPreviewRef"),
            ServerOnlyPayloadRef = ReadString(data, "serverOnlyPayloadRef"),
            Payload = ReadStringDictionary(data, "payload"),
            RedactionMetadata = ReadStringDictionary(data, "redactionMetadata"),
            ValidationSnapshot = ReadStringDictionary(data, "validationSnapshot"),
            BlockedReason = ReadNullableString(data, "blockedReason"),
            CancellationReason = ReadNullableString(data, "cancellationReason"),
            SchemaVersion = ReadString(data, "schemaVersion"),
            WroteData = false,
            Executed = false
        };
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

    private static string ReadString(IDictionary<string, object> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private static string? ReadNullableString(IDictionary<string, object> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static DateTimeOffset ReadDateTimeOffset(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return DateTimeOffset.UnixEpoch;
        }

        return value switch
        {
            Timestamp timestamp => timestamp.ToDateTimeOffset(),
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed : DateTimeOffset.UnixEpoch
        };
    }

    private static Timestamp ToTimestamp(DateTimeOffset value)
    {
        return Timestamp.FromDateTime(value.UtcDateTime);
    }

    private static IReadOnlyList<string> ReadStringList(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        return value is IEnumerable<object> values
            ? values.Select(item => item?.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
            : Array.Empty<string>();
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(
        IDictionary<string, object> data,
        string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return new Dictionary<string, string>();
        }

        if (value is IDictionary<string, object> objectValues)
        {
            return objectValues.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? string.Empty);
        }

        if (value is IDictionary<string, string> stringValues)
        {
            return new Dictionary<string, string>(stringValues);
        }

        return new Dictionary<string, string>();
    }
}
