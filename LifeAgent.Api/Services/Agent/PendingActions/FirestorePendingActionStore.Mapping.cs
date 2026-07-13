using Google.Cloud.Firestore;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed partial class FirestorePendingActionStore
{
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
            ["wroteData"] = record.WroteData,
            ["executed"] = record.Executed,
            ["isArchived"] = record.IsArchived,
            ["archivedAt"] = record.ArchivedAt is null ? null : ToTimestamp(record.ArchivedAt.Value),
            ["archivedByUserId"] = record.ArchivedByUserId
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
            WroteData = ReadBool(data, "wroteData"),
            Executed = ReadBool(data, "executed"),
            IsArchived = ReadBool(data, "isArchived"),
            ArchivedAt = ReadNullableDateTimeOffset(data, "archivedAt"),
            ArchivedByUserId = ReadNullableString(data, "archivedByUserId")
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
            WroteData = record.WroteData,
            Executed = record.Executed,
            IsArchived = record.IsArchived,
            ArchivedAt = record.ArchivedAt,
            ArchivedByUserId = record.ArchivedByUserId
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

    private static bool ReadBool(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool boolValue => boolValue,
            _ => bool.TryParse(value.ToString(), out var parsed) && parsed
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return ReadDateTimeOffset(data, key);
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
