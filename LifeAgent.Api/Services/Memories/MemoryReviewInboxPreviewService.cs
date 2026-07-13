using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryReviewInboxPreviewService : IMemoryReviewInboxPreviewService
{
    private const int MaxCandidateCount = 8;
    private const int MaxSourceCount = 3;
    private const int MaxSourceSnippetLength = 96;
    private const double CandidateConfidence = 0.82;

    public MemoryReviewInboxPreviewData BuildPreview(string userId, IReadOnlyList<LifeEvent> events)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required for memory review inbox preview.", nameof(userId));
        }

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

                return new MemoryReviewCandidateItem
                {
                    Id = $"review_{signal.Key}",
                    Type = signal.Type,
                    Title = signal.Title,
                    Detail = signal.Detail,
                    SourceEventIds = sourceIds,
                    Sources = sources,
                    Confidence = CandidateConfidence,
                    Reason = ordered.Count > 1 ? "最近多次出现" : "最近记录中出现",
                    PreviewOnly = true,
                    WroteData = false
                };
            })
            .OrderByDescending(candidate => candidate.SourceEventIds.Count)
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
                "你最近在持续整理 LifeOS 项目。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为长期项目线索。");
        }

        if (ContainsAny(text, "新疆"))
        {
            yield return Signal(
                lifeEvent,
                "xinjiang_travel_plan",
                "temporary_context",
                "你近期有去新疆的出行计划。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为近期背景线索。");
        }
        else if (ContainsAny(text, "无人机", "西安", "国家版本馆", "飞行", "出行", "路上"))
        {
            yield return Signal(
                lifeEvent,
                "travel_experience",
                "theme",
                "你最近在记录出行和新体验。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为生活主题线索。");
        }

        if (ContainsAny(text, "美食", "吃", "饺子", "蒸饺", "支出", "消费"))
        {
            yield return Signal(
                lifeEvent,
                "food_spending",
                "preference",
                "你最近会记录饮食体验和消费感受。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为偏好线索。");
        }

        if (ContainsAny(text, "骑车", "骑行", "心率", "运动", "身体"))
        {
            yield return Signal(
                lifeEvent,
                "body_movement",
                "habit",
                "你会关注运动状态和身体感受。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为习惯线索。");
        }

        if (ContainsAny(text, "kolon", "sports", "户外", "运动服饰"))
        {
            yield return Signal(
                lifeEvent,
                "sportswear_brand_interest",
                "preference",
                "你最近在关注户外/运动服饰品牌。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为偏好线索。");
        }

        if (ContainsAny(text, "版型") && ContainsAny(text, "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(
                lifeEvent,
                "style_price_hesitation",
                "preference",
                "你会在喜欢版型和价格偏高之间犹豫。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为购买偏好线索。");
        }
        else if (ContainsAny(text, "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(
                lifeEvent,
                "purchase_price",
                "preference",
                "你会权衡购买价格是否值得。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为消费偏好线索。");
        }

        if (ContainsAny(text, "喜欢", "不喜欢", "偏好"))
        {
            yield return Signal(
                lifeEvent,
                "explicit_preference",
                "preference",
                "你最近明确表达过一个偏好。",
                "来自最近的生活记录。之后如果你确认，我可以把它作为偏好线索。");
        }
    }

    private static ReviewSignal Signal(LifeEvent lifeEvent, string key, string type, string title, string detail)
    {
        return new ReviewSignal(key, type, title, detail, lifeEvent);
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

    private sealed record ReviewSignal(
        string Key,
        string Type,
        string Title,
        string Detail,
        LifeEvent LifeEvent)
    {
        public string SourceEventId => LifeEvent.Id;
        public DateTime OccurredAt => LifeEvent.OccurredAt;
    }
}
