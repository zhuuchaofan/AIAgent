using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryReviewInboxPreviewService : IMemoryReviewInboxPreviewService
{
    private const int MaxCandidateCount = 8;
    private const int MaxSourceCount = 3;
    private const int MaxSourceSnippetLength = 96;
    private const double OneOffConfidence = 0.62;
    private const double ObservingConfidence = 0.72;
    private const double StableConfidence = 0.86;

    private static readonly HashSet<string> GenericMatchFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "一个", "事情", "今天", "最近", "近期", "计划", "目标", "希望", "需要", "关注", "继续", "准备", "相关", "记住",
        "状态", "个人", "背景", "记录", "整理", "反复", "出现", "内容", "可能", "值得", "线索"
    };

    public MemoryReviewInboxPreviewData BuildPreview(
        string userId,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Memory>? activeMemories = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required for memory review inbox preview.", nameof(userId));
        }

        var memories = activeMemories ?? Array.Empty<Memory>();
        var candidates = events
            .SelectMany(BuildSignals)
            .GroupBy(signal => signal.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(signal => signal.OccurredAt)
                    .ThenBy(signal => signal.SourceEventId, StringComparer.Ordinal)
                    .ToList();
                var signal = ordered[0];
                var sources = ordered
                    .Select(item => BuildSource(item.LifeEvent))
                    .Where(source => !string.IsNullOrWhiteSpace(source.EventId))
                    .DistinctBy(source => source.EventId, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxSourceCount)
                    .ToArray();

                var sourceIds = sources
                    .Select(item => item.EventId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var stage = ToReviewStage(signal, sourceIds.Length);
                var isStable = stage == "stable";
                var candidateType = ToCandidateType(signal, isStable);
                var candidateTitle = ToCandidateTitle(signal, isStable);
                var relatedMemory = FindRelatedMemory(candidateTitle, signal, memories);
                var suggestedAction = relatedMemory is not null
                    ? "already_remembered"
                    : ToSuggestedAction(stage);
                var qualityReason = BuildQualityReason(stage, candidateType, sourceIds.Length, relatedMemory);

                return new MemoryReviewCandidateItem
                {
                    Id = $"review_{signal.Key}",
                    Type = candidateType,
                    Title = candidateTitle,
                    Detail = BuildDetail(candidateType, sourceIds.Length, stage),
                    ReviewStage = stage,
                    ReviewStageLabel = ToReviewStageLabel(stage),
                    SourceEventIds = sourceIds,
                    Sources = sources,
                    Confidence = ToConfidence(stage),
                    Reason = ToReason(stage),
                    QualityReason = qualityReason,
                    SuggestedAction = suggestedAction,
                    ReviewStatus = relatedMemory is null ? "pending" : "remembered",
                    MemoryId = relatedMemory?.Id,
                    PreviewOnly = true,
                    WroteData = false
                };
            })
            .OrderByDescending(candidate => candidate.ReviewStage == "stable")
            .ThenByDescending(candidate => candidate.ReviewStage == "observing")
            .ThenByDescending(candidate => candidate.SourceEventIds.Count)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Take(MaxCandidateCount)
            .ToList();

        return new MemoryReviewInboxPreviewData
        {
            ScannedCount = events.Count,
            PreviewOnly = true,
            WroteData = false,
            Candidates = candidates
        };
    }

    private static string ToReviewStage(ReviewSignal signal, int sourceCount)
    {
        if (sourceCount > 1)
        {
            return "stable";
        }

        return signal.Key switch
        {
            "food_spending" => "one_off",
            "purchase_price" => "one_off",
            "travel_experience" => "one_off",
            _ => "observing"
        };
    }

    private static string ToReviewStageLabel(string stage)
    {
        return stage switch
        {
            "stable" => "更稳定",
            "one_off" => "一次性",
            _ => "观察中"
        };
    }

    private static double ToConfidence(string stage)
    {
        return stage switch
        {
            "stable" => StableConfidence,
            "one_off" => OneOffConfidence,
            _ => ObservingConfidence
        };
    }

    private static string ToSuggestedAction(string stage)
    {
        return stage switch
        {
            "stable" => "review",
            "one_off" => "skip_one_off",
            _ => "keep_observing"
        };
    }

    private static string BuildQualityReason(
        string stage,
        string type,
        int sourceCount,
        Memory? relatedMemory)
    {
        if (relatedMemory is not null)
        {
            return "已有相近的已记住内容，不需要重复确认。";
        }

        if (stage == "stable")
        {
            var typeText = type switch
            {
                "preference" => "偏好",
                "habit" => "习惯",
                "goal" => "目标",
                "temporary_context" => "近期背景",
                _ => "主题"
            };
            return $"来自最近 {sourceCount} 条记录，已经更像稳定的{typeText}线索。";
        }

        if (stage == "one_off")
        {
            return "目前只来自一条记录，更像一次性事件，建议先跳过或继续观察。";
        }

        return "目前只来自一条记录，建议先观察，等更多记录出现后再决定。";
    }

    private static string ToReason(string stage)
    {
        return stage switch
        {
            "stable" => "最近多次出现",
            "one_off" => "可能只是一次性事件",
            _ => "近期线索"
        };
    }

    private static IEnumerable<ReviewSignal> BuildSignals(LifeEvent lifeEvent)
    {
        var text = BuildText(lifeEvent);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        if (ContainsAny(text, "codex", "lifeos", "项目", "整理"))
        {
            yield return Signal(
                lifeEvent,
                "project_work",
                "theme",
                "你最近在持续整理 LifeOS 项目。");
        }

        if (ContainsAny(text, "新疆"))
        {
            yield return Signal(
                lifeEvent,
                "xinjiang_travel_plan",
                "temporary_context",
                "你近期有去新疆的出行计划。");
        }
        else if (ContainsAny(text, "无人机", "西安", "国家版本馆", "飞行", "出行", "路上"))
        {
            yield return Signal(
                lifeEvent,
                "travel_experience",
                "theme",
                "你最近在记录出行和新体验。");
        }

        if (ContainsAny(text, "美食", "吃", "饺子", "蒸饺", "支出", "消费"))
        {
            yield return Signal(
                lifeEvent,
                "food_spending",
                "preference",
                "你最近会记录饮食体验和消费感受。");
        }

        if (ContainsAny(text, "骑车", "骑行", "心率", "运动", "身体"))
        {
            yield return Signal(
                lifeEvent,
                "body_movement",
                "habit",
                "你会关注运动状态和身体感受。");
        }

        if (ContainsAny(text, "kolon", "sports", "户外", "运动服饰"))
        {
            yield return Signal(
                lifeEvent,
                "sportswear_brand_interest",
                "preference",
                "你最近在关注户外/运动服饰品牌。");
        }

        if (ContainsAny(text, "版型") && ContainsAny(text, "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(
                lifeEvent,
                "style_price_hesitation",
                "preference",
                "你会在喜欢版型和价格偏高之间犹豫。");
        }
        else if (ContainsAny(text, "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(
                lifeEvent,
                "purchase_price",
                "preference",
                "你会权衡购买价格是否值得。");
        }

        if (ContainsAny(text, "喜欢", "不喜欢", "偏好"))
        {
            yield return Signal(
                lifeEvent,
                "explicit_preference",
                "preference",
                "你最近明确表达过一个偏好。");
        }
    }

    private static ReviewSignal Signal(LifeEvent lifeEvent, string key, string type, string title)
    {
        return new ReviewSignal(key, type, title, lifeEvent);
    }

    private static string ToCandidateType(ReviewSignal signal, bool isStable)
    {
        if (isStable)
        {
            return signal.Type;
        }

        return signal.Key switch
        {
            "body_movement" => "temporary_context",
            "food_spending" => "temporary_context",
            "travel_experience" => "temporary_context",
            _ => signal.Type
        };
    }

    private static string ToCandidateTitle(ReviewSignal signal, bool isStable)
    {
        if (isStable)
        {
            return signal.Title;
        }

        return signal.Key switch
        {
            "body_movement" => "你最近记录过运动状态和身体感受。",
            "food_spending" => "你最近记录过饮食体验和消费感受。",
            "travel_experience" => "你最近记录过出行和新体验。",
            _ => signal.Title
        };
    }

    private static string BuildDetail(string type, int sourceCount, string stage)
    {
        if (stage == "stable")
        {
            var typeText = type switch
            {
                "preference" => "偏好",
                "habit" => "习惯",
                "goal" => "目标",
                "temporary_context" => "近期背景",
                _ => "主题"
            };

            return $"来自最近 {sourceCount} 条生活记录，可以先作为较稳定的{typeText}线索观察。";
        }

        if (stage == "one_off")
        {
            return "来自最近一条生活记录，可能只是一次性事件。可以先观察，不建议直接当作长期记忆。";
        }

        return "来自最近一条生活记录，先作为线索观察，等更多记录出现后再判断是否值得记住。";
    }

    private static string BuildText(LifeEvent lifeEvent)
    {
        var raw = $"{lifeEvent.Title}。{lifeEvent.Content}";
        return CleanText(raw, int.MaxValue);
    }

    private static MemoryReviewSourceItem BuildSource(LifeEvent lifeEvent)
    {
        var title = CleanText(lifeEvent.Title, MaxSourceSnippetLength);
        var content = CleanText(lifeEvent.Content, MaxSourceSnippetLength);
        var snippet = string.IsNullOrWhiteSpace(content) || string.Equals(title, content, StringComparison.OrdinalIgnoreCase)
            ? title
            : content;

        return new MemoryReviewSourceItem
        {
            EventId = lifeEvent.Id,
            Title = string.IsNullOrWhiteSpace(title) ? "生活记录" : title,
            Snippet = snippet,
            OccurredAt = lifeEvent.OccurredAt
        };
    }

    private static string CleanText(string text, int maxLength)
    {
        var cleaned = Regex
            .Replace(text ?? string.Empty, @"\s+", " ")
            .Replace("用户输入：", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("用户输入:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        var technicalMarkers = new[]
        {
            "生活记录确认后写入 life_events",
            "提醒与工具操作仍不执行",
            "确认后会写入 life_events"
        };

        foreach (var marker in technicalMarkers)
        {
            var index = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                cleaned = cleaned[..index].Trim(' ', '。', '，', ',', ';', '；');
            }
        }

        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        return cleaned[..maxLength].TrimEnd() + "...";
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static Memory? FindRelatedMemory(
        string candidateTitle,
        ReviewSignal signal,
        IReadOnlyList<Memory> activeMemories)
    {
        if (activeMemories.Count == 0)
        {
            return null;
        }

        var candidateText = $"{candidateTitle} {signal.Title} {BuildText(signal.LifeEvent)}";
        return activeMemories
            .Where(memory => string.Equals(memory.Status, MemoryStatus.Active.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase))
            .Where(memory => !IsExpiredMemory(memory))
            .Where(memory => HasMeaningfulOverlap(candidateText, memory.Content))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
            .FirstOrDefault();
    }

    private static bool HasMeaningfulOverlap(string left, string right)
    {
        var leftFragments = ExtractMatchFragments(left);
        var rightFragments = ExtractMatchFragments(right);
        return leftFragments.Overlaps(rightFragments);
    }

    private static HashSet<string> ExtractMatchFragments(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"[A-Za-z0-9]+|[\u4e00-\u9fff]+"))
        {
            var token = match.Value.ToLowerInvariant();
            if (Regex.IsMatch(token, @"^[a-z0-9]+$") && token.Length >= 2)
            {
                result.Add(token);
                continue;
            }

            for (var index = 0; index < token.Length - 1; index++)
            {
                var fragment = token.Substring(index, 2);
                if (!GenericMatchFragments.Contains(fragment))
                {
                    result.Add(fragment);
                }
            }
        }

        return result;
    }

    private static bool IsExpiredMemory(Memory memory)
    {
        return memory.ExpiresAt.HasValue && memory.ExpiresAt.Value <= DateTime.UtcNow;
    }

    private sealed record ReviewSignal(
        string Key,
        string Type,
        string Title,
        LifeEvent LifeEvent)
    {
        public string SourceEventId => LifeEvent.Id;
        public DateTime OccurredAt => LifeEvent.OccurredAt;
    }
}
