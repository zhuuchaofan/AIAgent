using Google.Cloud.Firestore;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed partial class FirestorePendingActionStore
{
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
}
