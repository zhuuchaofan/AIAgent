using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryReviewInboxPreviewService : IMemoryReviewInboxPreviewService
{
    private const int MaxCandidateCount = 8;
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
                var sourceIds = ordered
                    .Select(item => item.SourceEventId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new MemoryReviewCandidateItem
                {
                    Id = $"review_{signal.Key}",
                    Type = signal.Type,
                    Title = signal.Title,
                    Detail = signal.Detail,
                    SourceEventIds = sourceIds,
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
            yield return Signal(lifeEvent, "project_work", "theme", "持续整理项目", "最近多次出现项目整理相关记录。");
        }

        if (ContainsAny(text, "无人机", "西安", "国家版本馆", "飞行", "出行", "新疆", "路上"))
        {
            yield return Signal(lifeEvent, "travel_experience", "theme", "出行和新体验", "最近记录里出现了出行、路上或新体验。");
        }

        if (ContainsAny(text, "美食", "吃", "饺子", "蒸饺", "支出", "消费"))
        {
            yield return Signal(lifeEvent, "food_spending", "preference", "饮食和消费感受", "最近记录里出现了饮食体验和消费感受。");
        }

        if (ContainsAny(text, "骑车", "骑行", "心率", "运动", "身体"))
        {
            yield return Signal(lifeEvent, "body_movement", "habit", "运动和身体状态", "最近记录里出现了运动状态或身体感受。");
        }

        if (ContainsAny(text, "kolon", "sports", "户外", "运动服饰", "版型", "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(lifeEvent, "sportswear_price", "preference", "运动服饰和价格权衡", "最近记录里出现了运动服饰关注和价格犹豫。");
        }

        if (ContainsAny(text, "喜欢", "不喜欢", "偏好"))
        {
            yield return Signal(lifeEvent, "explicit_preference", "preference", "明确表达过的偏好", "最近记录里有明确的喜欢、不喜欢或偏好表达。");
        }
    }

    private static ReviewSignal Signal(LifeEvent lifeEvent, string key, string type, string title, string detail)
    {
        return new ReviewSignal(key, type, title, detail, lifeEvent.Id, lifeEvent.OccurredAt);
    }

    private static string BuildText(LifeEvent lifeEvent)
    {
        var raw = $"{lifeEvent.Title}。{lifeEvent.Content}";
        return Regex
            .Replace(raw, @"\s+", " ")
            .Replace("用户输入：", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
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
        string SourceEventId,
        DateTime OccurredAt);
}
