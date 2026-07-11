namespace LifeAgent.Api.Services.Agent.PendingActions;

public interface IPendingActionStore
{
    Task<PendingActionStoreResult> CreateAsync(
        PendingActionCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PendingActionRecord?> GetByIdAsync(
        string userSubjectRef,
        string pendingActionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingActionClientView>> GetActiveByUserAsync(
        string userSubjectRef,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> UpdateStatusAsync(
        PendingActionStatusUpdate update,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> MarkExpiredAsync(
        string userSubjectRef,
        string pendingActionId,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> CancelAsync(
        string userSubjectRef,
        string pendingActionId,
        string? cancellationReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> ArchiveAsync(
        string userSubjectRef,
        string pendingActionId,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PendingActionStoreResult.Failed(
            "not_found",
            "not_found",
            "Pending action archive is not supported by this store."));
    }

    Task<PendingActionStoreResult> RecordConfirmationReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string confirmationId,
        string confirmationRequestHash,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> RecordGuardDecisionReferenceAsync(
        string userSubjectRef,
        string pendingActionId,
        string guardDecisionRef,
        string status,
        string? blockedReason = null,
        string? auditEventRef = null,
        CancellationToken cancellationToken = default);

    Task<PendingActionStoreResult> CheckIdempotencyKeyHashAsync(
        string userSubjectRef,
        string idempotencyKeyHash,
        CancellationToken cancellationToken = default);

    Task<PendingActionRecord?> GetByPreviewIdAsync(
        string userSubjectRef,
        string previewId,
        CancellationToken cancellationToken = default);

    Task<PendingActionRecord?> GetByConfirmationIdAsync(
        string userSubjectRef,
        string confirmationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingActionRecord>> QueryAsync(
        PendingActionQuery query,
        CancellationToken cancellationToken = default);
}
