namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed record PendingActionRecord
{
    public string PendingActionId { get; init; } = string.Empty;
    public string PreviewId { get; init; } = string.Empty;
    public string? ConfirmationId { get; init; }
    public string ToolId { get; init; } = string.Empty;
    public string ToolVersion { get; init; } = string.Empty;
    public string AdapterId { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string UserSubjectRef { get; init; } = string.Empty;
    public string SessionSubjectRef { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "medium_preview_only";
    public string Status { get; init; } = PendingActionStatus.PreviewCreated;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string IdempotencyKeyHash { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string PreviewHash { get; init; } = string.Empty;
    public string PolicySnapshotRef { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public IReadOnlyList<string> AuditEventRefs { get; init; } = Array.Empty<string>();
    public string SanitizedPreviewRef { get; init; } = string.Empty;
    public string ServerOnlyPayloadRef { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Payload { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> RedactionMetadata { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ValidationSnapshot { get; init; } = new Dictionary<string, string>();
    public string? BlockedReason { get; init; }
    public string? CancellationReason { get; init; }
    public string SchemaVersion { get; init; } = "phase7.9.pending_action.v1";
    public bool WroteData { get; init; }
    public bool Executed { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public string? ArchivedByUserId { get; init; }

    public PendingActionClientView ToClientView()
    {
        return new PendingActionClientView(
            PendingActionId,
            PreviewId,
            ActionType,
            Status,
            RiskLevel,
            CreatedAt,
            ExpiresAt,
            BlockedReason,
            TraceId,
            WroteData,
            Executed,
            IsArchived);
    }
}

public sealed record PendingActionClientView(
    string PendingActionId,
    string PreviewId,
    string ActionType,
    string Status,
    string RiskLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? BlockedReason,
    string TraceId,
    bool WroteData,
    bool Executed,
    bool IsArchived);
