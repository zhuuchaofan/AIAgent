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
    string? IdempotencyKeyHash = null,
    bool? WroteData = null,
    bool? Executed = null);

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
    bool ActiveOnly = false,
    bool IncludeArchived = false);

internal static class PendingActionCreateRequestValidator
{
    public static PendingActionStoreResult? Validate(PendingActionCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserSubjectRef) ||
            string.IsNullOrWhiteSpace(request.PendingActionId))
        {
            return PendingActionStoreResult.Failed(
                "invalid_request",
                "invalid_request",
                "Pending action requires an authenticated owner and id.");
        }

        if (string.IsNullOrWhiteSpace(request.PreviewId) ||
            string.IsNullOrWhiteSpace(request.ToolId) ||
            string.IsNullOrWhiteSpace(request.ToolVersion) ||
            string.IsNullOrWhiteSpace(request.AdapterId) ||
            string.IsNullOrWhiteSpace(request.ActionType) ||
            string.IsNullOrWhiteSpace(request.SessionSubjectRef) ||
            string.IsNullOrWhiteSpace(request.RiskLevel) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKeyHash) ||
            string.IsNullOrWhiteSpace(request.InputHash) ||
            string.IsNullOrWhiteSpace(request.PreviewHash) ||
            string.IsNullOrWhiteSpace(request.PolicySnapshotRef) ||
            string.IsNullOrWhiteSpace(request.TraceId) ||
            string.IsNullOrWhiteSpace(request.SanitizedPreviewRef) ||
            string.IsNullOrWhiteSpace(request.ServerOnlyPayloadRef))
        {
            return PendingActionStoreResult.Failed(
                "invalid_request",
                "invalid_audit_metadata",
                "Pending action requires complete audit metadata before it can be stored.");
        }

        if (request.AuditEventRefs.Count == 0 ||
            request.AuditEventRefs.Any(string.IsNullOrWhiteSpace))
        {
            return PendingActionStoreResult.Failed(
                "invalid_request",
                "invalid_audit_metadata",
                "Pending action requires at least one audit event reference.");
        }

        return null;
    }
}
