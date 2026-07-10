namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed record PendingActionCreateRequest(
    string PendingActionId,
    string PreviewId,
    string ToolId,
    string ToolVersion,
    string AdapterId,
    string ActionType,
    string UserSubjectRef,
    string SessionSubjectRef,
    string RiskLevel,
    DateTimeOffset ExpiresAt,
    string IdempotencyKeyHash,
    string InputHash,
    string PreviewHash,
    string PolicySnapshotRef,
    string TraceId,
    IReadOnlyList<string> AuditEventRefs,
    string SanitizedPreviewRef,
    string ServerOnlyPayloadRef,
    IReadOnlyDictionary<string, string>? Payload = null,
    IReadOnlyDictionary<string, string>? RedactionMetadata = null,
    IReadOnlyDictionary<string, string>? ValidationSnapshot = null);

public sealed record PendingActionStatusUpdate(
    string PendingActionId,
    string UserSubjectRef,
    string ExpectedStatus,
    string NewStatus,
    string? ConfirmationId = null,
    string? BlockedReason = null,
    string? CancellationReason = null,
    string? AuditEventRef = null,
    string? IdempotencyKeyHash = null);

public sealed record PendingActionStoreResult(
    bool Success,
    string Status,
    PendingActionRecord? Record,
    bool Idempotent = false,
    string? ErrorCode = null,
    string? Message = null)
{
    public static PendingActionStoreResult Succeeded(PendingActionRecord record, bool idempotent = false)
    {
        return new PendingActionStoreResult(true, record.Status, record, idempotent);
    }

    public static PendingActionStoreResult Failed(string status, string errorCode, string message)
    {
        return new PendingActionStoreResult(false, status, null, false, errorCode, message);
    }
}

public sealed record PendingActionQuery(
    string UserSubjectRef,
    string? Status = null,
    string? PreviewId = null,
    string? ConfirmationId = null,
    string? TraceId = null,
    string? IdempotencyKeyHash = null,
    bool ActiveOnly = false);
