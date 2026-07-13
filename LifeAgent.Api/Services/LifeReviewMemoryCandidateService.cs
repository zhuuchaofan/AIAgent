using System.Security.Cryptography;
using System.Text;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Api.Services;

public sealed class LifeReviewMemoryCandidateService : ILifeReviewMemoryCandidateService
{
    private const int SourceLookupLimit = 50;

    private readonly ILifeEventService _lifeEventService;
    private readonly IMemoryReviewStateStore _memoryReviewStateStore;

    public LifeReviewMemoryCandidateService(
        ILifeEventService lifeEventService,
        IMemoryReviewStateStore memoryReviewStateStore)
    {
        _lifeEventService = lifeEventService;
        _memoryReviewStateStore = memoryReviewStateStore;
    }

    public async Task<MemoryReviewCandidateActionResponse> KeepFromReviewCardAsync(
        string userId,
        LifeReviewKeepRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Text))
        {
            throw new InvalidInputException("回顾内容不能为空");
        }

        var requestedSourceIds = (request.SourceEventIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (requestedSourceIds.Length == 0)
        {
            throw new InvalidInputException("这张回顾卡暂时缺少依据记录，不能放入记忆候选。");
        }

        var eventsResult = await _lifeEventService.ListEventsAsync(
            userId,
            type: "all",
            limit: SourceLookupLimit,
            cursor: null,
            tag: null);

        var sourceEvents = eventsResult.Data
            .Where(item => !item.IsDeleted)
            .Where(item => requestedSourceIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(item => item.OccurredAt == default ? item.CreatedAt : item.OccurredAt)
            .ToArray();

        if (sourceEvents.Length == 0)
        {
            throw new InvalidInputException("没有找到这张回顾卡对应的生活记录。");
        }

        var candidate = BuildCandidate(request, sourceEvents);
        var state = await _memoryReviewStateStore.UpsertAsync(
            userId,
            new MemoryReviewStateUpsertRequest(candidate, "kept", candidate.MemoryId));
        var updated = MemoryReviewInboxStateProjection.ApplyState(
            candidate,
            new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
            {
                [candidate.Id] = state
            });

        return new MemoryReviewCandidateActionResponse
        {
            Success = true,
            PreviewOnly = true,
            MemoryWriteEnabled = false,
            WroteMemory = false,
            WroteReviewState = true,
            Data = updated
        };
    }

    private static MemoryReviewCandidateItem BuildCandidate(
        LifeReviewKeepRequest request,
        IReadOnlyList<LifeEvent> sourceEvents)
    {
        var cardId = NormalizeCardId(request.CardId);
        var text = TrimForDisplay(request.Text, 140);
        var title = string.IsNullOrWhiteSpace(request.Title) ? "最近回顾" : TrimForDisplay(request.Title, 40);
        var isStable = sourceEvents.Count > 1;
        var type = ToMemoryCandidateType(cardId);
        var sourceItems = sourceEvents.Select(ToSourceItem).ToArray();
        var sourceIds = sourceItems.Select(source => source.EventId).ToArray();

        return new MemoryReviewCandidateItem
        {
            Id = BuildCandidateId(cardId, text, sourceIds),
            Type = type,
            Title = text,
            Detail = $"来自最近回顾「{title}」，已先放到可能值得记住的事情里。",
            ReviewStage = isStable ? "stable" : "observing",
            ReviewStageLabel = isStable ? "更稳定" : "观察中",
            SourceEventIds = sourceIds,
            Sources = sourceItems,
            Confidence = isStable ? 0.84 : 0.74,
            Reason = "来自最近回顾",
            ReviewStatus = "kept",
            PreviewOnly = true,
            WroteData = false
        };
    }

    private static MemoryReviewSourceItem ToSourceItem(LifeEvent lifeEvent)
    {
        var occurredAt = lifeEvent.OccurredAt == default ? lifeEvent.CreatedAt : lifeEvent.OccurredAt;
        return new MemoryReviewSourceItem
        {
            EventId = lifeEvent.Id,
            Title = TrimForDisplay(lifeEvent.Title, 80, fallback: "生活记录"),
            Snippet = TrimForDisplay(lifeEvent.Content, 110, fallback: lifeEvent.Title),
            OccurredAt = occurredAt
        };
    }

    private static string ToMemoryCandidateType(string cardId)
    {
        return cardId switch
        {
            "repeated_themes" => "theme",
            "upcoming_plans" => "temporary_context",
            "worth_noticing" => "preference",
            _ => "temporary_context"
        };
    }

    private static string BuildCandidateId(string cardId, string text, IReadOnlyList<string> sourceIds)
    {
        var raw = $"{cardId}|{text}|{string.Join(",", sourceIds.OrderBy(id => id, StringComparer.Ordinal))}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hash = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"review_card_{cardId}_{hash}";
    }

    private static string NormalizeCardId(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "recent_state" : value.Trim().ToLowerInvariant();
        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var result = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "recent_state" : result;
    }

    private static string TrimForDisplay(string? value, int maxLength, string? fallback = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback?.Trim() : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }
}
