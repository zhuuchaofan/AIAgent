using System.Text.Json;
using Google.Cloud.Firestore;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public class FirestorePendingAgentActionStore : IPendingAgentActionStore
{
    public const string Created = InMemoryPendingAgentActionStore.Created;
    public const string Pending = InMemoryPendingAgentActionStore.Pending;
    public const string Confirmed = InMemoryPendingAgentActionStore.Confirmed;
    public const string Cancelled = InMemoryPendingAgentActionStore.Cancelled;
    public const string Expired = InMemoryPendingAgentActionStore.Expired;

    private static readonly HashSet<string> AllowedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "save_memory_preview",
        "create_life_event_preview",
        "create_reminder_preview"
    };

    private readonly FirestoreDb _db;

    public FirestorePendingAgentActionStore(FirestoreDb db)
    {
        _db = db;
    }

    public async Task<PendingAgentAction> CreateAsync(
        string userId,
        string actionType,
        string title,
        string summary,
        object payload,
        string riskLevel,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedActionTypes.Contains(actionType))
        {
            throw new InvalidOperationException($"Unknown proposed action type: {actionType}");
        }

        var now = DateTime.UtcNow;
        var actionId = $"agent_action_{Guid.NewGuid():N}";
        var expiresAt = now.Add(ttl);
        var document = new PendingAgentActionDocument
        {
            ActionId = actionId,
            UserId = userId,
            ActionType = actionType,
            Title = title,
            Summary = summary,
            PayloadJson = JsonSerializer.Serialize(payload),
            RiskLevel = riskLevel,
            RequiresConfirmation = true,
            Status = Pending,
            LifecycleStatus = Pending,
            PreviewOnly = true,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt
        };

        await GetDocument(userId, actionId).SetAsync(document, cancellationToken: cancellationToken);
        return document.ToModel();
    }

    public async Task<AgentConfirmationResponse> ConfirmAsync(
        string userId,
        string actionId,
        string decision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return Failed(actionId, "not_found", "Pending action was not found.");
        }

        var normalizedDecision = (decision ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedDecision is not ("confirm" or "cancel"))
        {
            return Failed(actionId, "invalid_decision", "Decision must be confirm or cancel.");
        }

        var docRef = GetDocument(userId, actionId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            if (!snapshot.Exists)
            {
                return Failed(actionId, "not_found", "Pending action was not found.");
            }

            var pending = snapshot.ConvertTo<PendingAgentActionDocument>();
            if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
            {
                return Failed(actionId, "not_found", "Pending action was not found.");
            }

            if (!AllowedActionTypes.Contains(pending.ActionType))
            {
                return Failed(actionId, "invalid_action_type", "Unknown proposed action type.");
            }

            if (pending.Status is Confirmed or Cancelled)
            {
                if (IsSameTerminalDecision(pending.Status, normalizedDecision))
                {
                    return PreviewSuccess(pending, pending.Status, idempotent: true);
                }

                return Failed(actionId, pending.Status, $"Pending action is already {pending.Status}.");
            }

            if (pending.Status == Expired)
            {
                return Failed(actionId, Expired, "Pending action expired.");
            }

            var now = DateTime.UtcNow;
            if (pending.ExpiresAt <= now)
            {
                pending.Status = Expired;
                pending.LifecycleStatus = Expired;
                pending.UpdatedAt = now;
                pending.ExpiredAt = now;
                transaction.Set(docRef, pending);
                return Failed(actionId, Expired, "Pending action expired.");
            }

            pending.Status = normalizedDecision == "confirm" ? Confirmed : Cancelled;
            pending.LifecycleStatus = pending.Status;
            pending.UpdatedAt = now;
            if (pending.Status == Confirmed)
            {
                pending.ConfirmedAt = now;
            }
            else
            {
                pending.CancelledAt = now;
            }

            transaction.Set(docRef, pending);
            return PreviewSuccess(pending, pending.Status, idempotent: false);
        }, cancellationToken: cancellationToken);
    }

    private DocumentReference GetDocument(string userId, string actionId)
    {
        return _db.Collection("users")
            .Document(userId)
            .Collection("agent_pending_actions")
            .Document(actionId);
    }

    private static AgentConfirmationResponse PreviewSuccess(PendingAgentActionDocument pending, string status, bool idempotent)
    {
        var message = status == Cancelled
            ? "Agent action preview cancelled. No data was written."
            : "Agent action preview confirmed. No data was written in Phase 4.7.";

        return new AgentConfirmationResponse
        {
            Success = true,
            Status = status,
            Message = message,
            ActionId = pending.ActionId,
            ActionType = pending.ActionType,
            LifecycleStatus = pending.LifecycleStatus,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending.ActionType,
                idempotent
            }
        };
    }

    private static AgentConfirmationResponse Failed(string? actionId, string status, string message)
    {
        return new AgentConfirmationResponse
        {
            Success = false,
            Status = status,
            Message = message,
            ActionId = actionId
        };
    }

    private static bool IsSameTerminalDecision(string status, string decision)
    {
        return status == Confirmed && decision == "confirm" ||
               status == Cancelled && decision == "cancel";
    }

    [FirestoreData]
    private sealed class PendingAgentActionDocument
    {
        [FirestoreDocumentId]
        public string ActionId { get; set; } = string.Empty;

        [FirestoreProperty("userId")]
        public string UserId { get; set; } = string.Empty;

        [FirestoreProperty("actionType")]
        public string ActionType { get; set; } = string.Empty;

        [FirestoreProperty("title")]
        public string Title { get; set; } = string.Empty;

        [FirestoreProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [FirestoreProperty("payloadJson")]
        public string PayloadJson { get; set; } = "{}";

        [FirestoreProperty("riskLevel")]
        public string RiskLevel { get; set; } = "medium";

        [FirestoreProperty("requiresConfirmation")]
        public bool RequiresConfirmation { get; set; } = true;

        [FirestoreProperty("status")]
        public string Status { get; set; } = Created;

        [FirestoreProperty("lifecycleStatus")]
        public string LifecycleStatus { get; set; } = Created;

        [FirestoreProperty("previewOnly")]
        public bool PreviewOnly { get; set; } = true;

        [FirestoreProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [FirestoreProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        [FirestoreProperty("confirmedAt")]
        public DateTime? ConfirmedAt { get; set; }

        [FirestoreProperty("cancelledAt")]
        public DateTime? CancelledAt { get; set; }

        [FirestoreProperty("expiredAt")]
        public DateTime? ExpiredAt { get; set; }

        [FirestoreProperty("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        public PendingAgentAction ToModel()
        {
            return new PendingAgentAction
            {
                UserId = UserId,
                Status = Status,
                PreviewOnly = PreviewOnly,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)),
                ConfirmedAt = ToDateTimeOffset(ConfirmedAt),
                CancelledAt = ToDateTimeOffset(CancelledAt),
                ExpiredAt = ToDateTimeOffset(ExpiredAt),
                ProposedAction = new AgentProposedAction
                {
                    ActionId = ActionId,
                    ActionType = ActionType,
                    Title = Title,
                    Summary = Summary,
                    Payload = DeserializePayload(PayloadJson),
                    RiskLevel = RiskLevel,
                    RequiresConfirmation = RequiresConfirmation,
                    LifecycleStatus = LifecycleStatus,
                    CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
                    ExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(ExpiresAt, DateTimeKind.Utc))
                }
            };
        }

        private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
        {
            return value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null;
        }

        private static object DeserializePayload(string payloadJson)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(payloadJson);
            }
            catch (JsonException)
            {
                return new { };
            }
        }
    }
}
