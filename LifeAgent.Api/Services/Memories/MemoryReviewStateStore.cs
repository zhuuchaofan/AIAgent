using Google.Cloud.Firestore;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryReviewStateStore
{
    Task<IReadOnlyDictionary<string, MemoryReviewStateRecord>> ListByCandidateIdsAsync(
        string userId,
        IReadOnlyList<string> candidateIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryReviewCandidateItem>> ListKeptCandidatesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<MemoryReviewStateRecord> UpsertAsync(
        string userId,
        MemoryReviewStateUpsertRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MemoryReviewStateUpsertRequest(
    MemoryReviewCandidateItem Candidate,
    string Status);

public sealed record MemoryReviewStateRecord(
    string CandidateId,
    string Status,
    DateTime UpdatedAt,
    DateTime? ReviewedAt);

public sealed class FirestoreMemoryReviewStateStore : IMemoryReviewStateStore
{
    private const string CollectionName = "memory_review_items";
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "kept",
        "dismissed"
    };

    private readonly FirestoreDb _db;
    private readonly TimeProvider _timeProvider;

    public FirestoreMemoryReviewStateStore(FirestoreDb db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyDictionary<string, MemoryReviewStateRecord>> ListByCandidateIdsAsync(
        string userId,
        IReadOnlyList<string> candidateIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var distinctIds = candidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidateId in distinctIds)
        {
            var snapshot = await UserCollection(userId)
                .Document(candidateId)
                .GetSnapshotAsync(cancellationToken);

            if (snapshot.Exists)
            {
                var record = ToRecord(candidateId, snapshot.ToDictionary());
                if (record != null)
                {
                    result[candidateId] = record;
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<MemoryReviewCandidateItem>> ListKeptCandidatesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var snapshot = await UserCollection(userId)
            .WhereEqualTo("status", "kept")
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents
            .Select(document => ToCandidate(document.Id, document.ToDictionary()))
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.ReviewedAt ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<MemoryReviewStateRecord> UpsertAsync(
        string userId,
        MemoryReviewStateUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(request.Candidate.Id))
        {
            throw new ArgumentException("candidate id is required.", nameof(request));
        }

        if (!AllowedStatuses.Contains(request.Status))
        {
            throw new ArgumentException($"Unsupported memory review status: {request.Status}", nameof(request));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var docRef = UserCollection(userId).Document(request.Candidate.Id);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        var data = BuildData(request.Candidate, request.Status, now, includeCreatedAt: !snapshot.Exists);

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);

        return new MemoryReviewStateRecord(
            request.Candidate.Id,
            request.Status,
            now,
            request.Status is "kept" or "dismissed" ? now : null);
    }

    private CollectionReference UserCollection(string userId)
    {
        return _db.Collection("users")
            .Document(userId)
            .Collection(CollectionName);
    }

    private static Dictionary<string, object> BuildData(
        MemoryReviewCandidateItem candidate,
        string status,
        DateTime now,
        bool includeCreatedAt)
    {
        var data = new Dictionary<string, object>
        {
            ["candidateId"] = candidate.Id,
            ["status"] = status,
            ["type"] = candidate.Type,
            ["title"] = candidate.Title,
            ["detail"] = candidate.Detail,
            ["reviewStage"] = candidate.ReviewStage,
            ["reviewStageLabel"] = candidate.ReviewStageLabel,
            ["confidence"] = candidate.Confidence,
            ["reason"] = candidate.Reason,
            ["sourceEventIds"] = candidate.SourceEventIds.ToArray(),
            ["sourceTitles"] = candidate.Sources.Select(source => source.Title).Distinct().ToArray(),
            ["sources"] = candidate.Sources.Select(source => new Dictionary<string, object?>
            {
                ["eventId"] = source.EventId,
                ["title"] = source.Title,
                ["snippet"] = source.Snippet,
                ["occurredAt"] = Timestamp.FromDateTime(DateTime.SpecifyKind(source.OccurredAt.ToUniversalTime(), DateTimeKind.Utc))
            }).ToArray(),
            ["updatedAt"] = Timestamp.FromDateTime(DateTime.SpecifyKind(now, DateTimeKind.Utc)),
            ["reviewedAt"] = Timestamp.FromDateTime(DateTime.SpecifyKind(now, DateTimeKind.Utc))
        };

        if (includeCreatedAt)
        {
            data["createdAt"] = Timestamp.FromDateTime(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        }

        return data;
    }

    private static MemoryReviewStateRecord? ToRecord(string fallbackCandidateId, Dictionary<string, object> data)
    {
        var candidateId = data.TryGetValue("candidateId", out var candidateIdValue)
            ? candidateIdValue as string
            : fallbackCandidateId;
        var status = data.TryGetValue("status", out var statusValue)
            ? statusValue as string
            : null;

        if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return new MemoryReviewStateRecord(
            candidateId,
            status,
            ToDateTime(data, "updatedAt") ?? DateTime.MinValue,
            ToDateTime(data, "reviewedAt"));
    }

    private static DateTime? ToDateTime(Dictionary<string, object> data, string field)
    {
        if (!data.TryGetValue(field, out var value))
        {
            return null;
        }

        return value switch
        {
            Timestamp timestamp => timestamp.ToDateTime(),
            DateTime dateTime => dateTime.ToUniversalTime(),
            _ => null
        };
    }

    private static MemoryReviewCandidateItem? ToCandidate(string fallbackCandidateId, Dictionary<string, object> data)
    {
        var candidateId = ReadString(data, "candidateId") ?? fallbackCandidateId;
        var status = ReadString(data, "status") ?? "pending";
        var title = ReadString(data, "title");

        if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var sources = ReadSources(data);
        var sourceEventIds = ReadStringArray(data, "sourceEventIds");

        return new MemoryReviewCandidateItem
        {
            Id = candidateId,
            Type = ReadString(data, "type") ?? "theme",
            Title = title,
            Detail = ReadString(data, "detail") ?? string.Empty,
            ReviewStage = ReadString(data, "reviewStage") ?? "observing",
            ReviewStageLabel = ReadString(data, "reviewStageLabel") ?? "观察中",
            SourceEventIds = sourceEventIds.Length > 0
                ? sourceEventIds
                : sources.Select(source => source.EventId).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
            Sources = sources,
            Confidence = ReadDouble(data, "confidence"),
            Reason = ReadString(data, "reason") ?? "已留着",
            ReviewStatus = status,
            ReviewedAt = ToDateTime(data, "reviewedAt"),
            PreviewOnly = true,
            WroteData = false
        };
    }

    private static string? ReadString(Dictionary<string, object> data, string field)
    {
        return data.TryGetValue(field, out var value) ? value as string : null;
    }

    private static double ReadDouble(Dictionary<string, object> data, string field)
    {
        return data.TryGetValue(field, out var value) ? Convert.ToDouble(value) : 0;
    }

    private static string[] ReadStringArray(Dictionary<string, object> data, string field)
    {
        if (!data.TryGetValue(field, out var value) || value is not IEnumerable<object> values)
        {
            return Array.Empty<string>();
        }

        return values
            .OfType<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IReadOnlyList<MemoryReviewSourceItem> ReadSources(Dictionary<string, object> data)
    {
        if (data.TryGetValue("sources", out var sourceValue) && sourceValue is IEnumerable<object> sourceValues)
        {
            return sourceValues
                .OfType<Dictionary<string, object>>()
                .Select(source => new MemoryReviewSourceItem
                {
                    EventId = ReadString(source, "eventId") ?? string.Empty,
                    Title = ReadString(source, "title") ?? "生活记录",
                    Snippet = ReadString(source, "snippet") ?? string.Empty,
                    OccurredAt = ToDateTime(source, "occurredAt") ?? DateTime.MinValue
                })
                .ToArray();
        }

        var eventIds = ReadStringArray(data, "sourceEventIds");
        var titles = ReadStringArray(data, "sourceTitles");
        return eventIds
            .Select((eventId, index) => new MemoryReviewSourceItem
            {
                EventId = eventId,
                Title = index < titles.Length ? titles[index] : "生活记录",
                Snippet = string.Empty,
                OccurredAt = DateTime.MinValue
            })
            .ToArray();
    }
}

public static class MemoryReviewInboxStateProjection
{
    public static MemoryReviewInboxPreviewData Apply(
        MemoryReviewInboxPreviewData preview,
        IReadOnlyDictionary<string, MemoryReviewStateRecord> states,
        bool includeDismissed = false)
    {
        var candidates = preview.Candidates
            .Select(candidate => ApplyState(candidate, states))
            .Where(candidate => includeDismissed || !string.Equals(candidate.ReviewStatus, "dismissed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new MemoryReviewInboxPreviewData
        {
            ScannedCount = preview.ScannedCount,
            PreviewOnly = preview.PreviewOnly,
            WroteData = preview.WroteData,
            MemoryWriteEnabled = preview.MemoryWriteEnabled,
            Candidates = candidates
        };
    }

    public static MemoryReviewInboxPreviewData AddMissingKeptCandidates(
        MemoryReviewInboxPreviewData preview,
        IReadOnlyList<MemoryReviewCandidateItem> keptCandidates)
    {
        var existingIds = preview.Candidates
            .Select(candidate => candidate.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = preview.Candidates
            .Concat(keptCandidates.Where(candidate => !existingIds.Contains(candidate.Id)))
            .ToArray();

        return new MemoryReviewInboxPreviewData
        {
            ScannedCount = preview.ScannedCount,
            PreviewOnly = preview.PreviewOnly,
            WroteData = preview.WroteData,
            MemoryWriteEnabled = preview.MemoryWriteEnabled,
            Candidates = merged
        };
    }

    public static MemoryReviewCandidateItem ApplyState(
        MemoryReviewCandidateItem candidate,
        IReadOnlyDictionary<string, MemoryReviewStateRecord> states)
    {
        if (!states.TryGetValue(candidate.Id, out var state))
        {
            candidate.ReviewStatus = "pending";
            candidate.ReviewedAt = null;
            return candidate;
        }

        candidate.ReviewStatus = string.IsNullOrWhiteSpace(state.Status) ? "pending" : state.Status;
        candidate.ReviewedAt = state.ReviewedAt;
        return candidate;
    }
}
