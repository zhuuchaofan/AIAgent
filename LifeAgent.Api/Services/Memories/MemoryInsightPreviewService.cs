using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryInsightPreviewService : IMemoryInsightPreviewService
{
    private const int MaxInsightCount = 3;
    private const int MaxInsightTextLength = 72;
    private const double AggregatedInsightConfidence = 0.84;
    private const double RepeatedThemeWeight = 2.0;
    private const double RecentVisibleWeight = 3.0;
    private readonly IMemoryExtractionService _memoryExtractionService;

    public MemoryInsightPreviewService(IMemoryExtractionService memoryExtractionService)
    {
        _memoryExtractionService = memoryExtractionService;
    }

    public MemoryInsightPreviewData BuildPreview(string userId, IReadOnlyList<LifeEvent> events)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required for memory insight preview.", nameof(userId));
        }

        var aggregatedInsights = BuildAggregatedInsights(events);

        var fallbackInsights = BuildFallbackInsights(userId, events)
            .Where(insight => aggregatedInsights.All(existing =>
                !string.Equals(
                    NormalizeForDedupe(existing.Text),
                    NormalizeForDedupe(insight.Text),
                    StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var insights = aggregatedInsights
            .Concat(fallbackInsights)
            .Take(MaxInsightCount)
            .ToList();

        return new MemoryInsightPreviewData
        {
            ScannedCount = events.Count,
            PreviewOnly = true,
            WroteData = false,
            MemoryWriteEnabled = false,
            Insights = insights
        };
    }

    private IReadOnlyList<MemoryInsightPreviewItem> BuildFallbackInsights(
        string userId,
        IReadOnlyList<LifeEvent> events)
    {
        var timelineItems = events
            .Select(e => new TimelineMemoryExtractionInput
            {
                EventId = e.Id,
                Type = e.Type,
                Title = string.Empty,
                Content = BuildExtractionText(e),
                OccurredAt = e.OccurredAt,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = "life_event"
                }
            })
            .ToList();

        var extractionResults = _memoryExtractionService.Extract(new MemoryExtractionRequest
        {
            UserId = userId,
            TimelineItems = timelineItems
        });

        var insights = extractionResults
            .Where(result =>
                (string.Equals(result.Status, "proposed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(result.Status, "review_required", StringComparison.OrdinalIgnoreCase)) &&
                result.Proposal is not null)
            .Select(ToInsight)
            .Where(insight => !string.IsNullOrWhiteSpace(insight.Text))
            .GroupBy(insight => $"{insight.Kind}:{NormalizeForDedupe(insight.Text)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaxInsightCount)
            .ToList();

        return insights;
    }

    private static IReadOnlyList<MemoryInsightPreviewItem> BuildAggregatedInsights(IReadOnlyList<LifeEvent> events)
    {
        return events
            .SelectMany((lifeEvent, index) => BuildSignals(lifeEvent, index))
            .GroupBy(signal => signal.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(signal => signal.OccurredAt)
                    .ThenBy(signal => signal.SourceEventId, StringComparer.Ordinal)
                    .ToList();

                return new
                {
                    Signal = ordered[0],
                    Count = ordered.Count,
                    Score = ordered.Sum(signal => signal.Score),
                    SourceIds = ordered
                        .Select(signal => signal.SourceEventId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            })
            .OrderByDescending(group => group.Score)
            .ThenByDescending(group => group.Count)
            .ThenByDescending(group => group.Signal.OccurredAt)
            .Take(MaxInsightCount)
            .Select(group => new MemoryInsightPreviewItem
            {
                Kind = group.Signal.Kind,
                Text = group.Signal.Text,
                Confidence = AggregatedInsightConfidence,
                SourceEventIds = group.SourceIds
            })
            .ToList();
    }

    private static IEnumerable<InsightSignal> BuildSignals(LifeEvent lifeEvent, int index)
    {
        var text = BuildExtractionText(lifeEvent);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var score = RepeatedThemeWeight + Math.Max(0, 3 - index) * RecentVisibleWeight;

        if (ContainsAny(text, "codex", "lifeos", "项目", "整理"))
        {
            yield return Signal(lifeEvent, "project_work", "theme", "你最近在持续整理项目相关的事情。", score);
        }

        if (ContainsAny(text, "新疆"))
        {
            yield return Signal(lifeEvent, "xinjiang_travel_plan", "temporary_context", "你近期有去新疆的出行计划。", score + 1.0);
        }
        else if (ContainsAny(text, "无人机", "西安", "国家版本馆", "飞行", "出行", "路上"))
        {
            yield return Signal(lifeEvent, "travel_experience", "theme", "你最近在记录出行和新体验。", score);
        }

        if (ContainsAny(text, "美食", "吃", "饺子", "蒸饺", "支出", "消费"))
        {
            yield return Signal(lifeEvent, "food_spending", "preference", "你最近在记录饮食和消费感受。", score);
        }

        if (ContainsAny(text, "骑车", "骑行", "心率", "运动", "身体"))
        {
            yield return Signal(lifeEvent, "body_movement", "theme", "你最近在关注运动状态和身体感受。", score);
        }

        if (ContainsAny(text, "kolon", "sports", "户外", "运动服饰", "版型") &&
            ContainsAny(text, "价格", "贵", "购买", "值得", "划算"))
        {
            yield return Signal(lifeEvent, "sportswear_price", "preference", "你最近在关注运动服饰，也在权衡价格。", score + 2.0);
        }
        else if (ContainsAny(text, "价格", "贵", "购买", "值得", "划算", "版型"))
        {
            yield return Signal(lifeEvent, "purchase_price", "preference", "你最近在权衡购买和价格。", score);
        }
    }

    private static InsightSignal Signal(LifeEvent lifeEvent, string key, string kind, string text, double score)
    {
        return new InsightSignal(
            key,
            kind,
            text,
            lifeEvent.Id,
            lifeEvent.OccurredAt,
            score);
    }

    private static MemoryInsightPreviewItem ToInsight(MemoryExtractionResult result)
    {
        var proposal = result.Proposal!;
        var kind = ToProductKind(proposal.MemoryType);
        var content = CleanText(proposal.Content);

        return new MemoryInsightPreviewItem
        {
            Kind = kind,
            Text = ToProductText(kind, content),
            Confidence = proposal.Confidence,
            SourceEventIds = string.IsNullOrWhiteSpace(result.SourceId)
                ? Array.Empty<string>()
                : new[] { result.SourceId }
        };
    }

    private static string ToProductKind(string memoryType)
    {
        return memoryType switch
        {
            "preference" => "preference",
            "habit" => "habit",
            "goal" => "goal",
            "temporary_context" => "temporary_context",
            "constraint" => "preference",
            _ => "theme"
        };
    }

    private static string ToProductText(string kind, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return kind switch
        {
            "preference" => InferPreferenceText(content),
            "habit" => InferHabitText(content),
            "goal" => InferGoalText(content),
            "temporary_context" => InferTemporaryContextText(content),
            _ => InferThemeText(content)
        };
    }

    private static string BuildExtractionText(LifeEvent lifeEvent)
    {
        var title = CleanText(lifeEvent.Title);
        var content = CleanText(lifeEvent.Content);

        if (string.IsNullOrWhiteSpace(title))
        {
            return content;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return title;
        }

        if (string.Equals(title, content, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        if (content.Contains(title, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        if (title.Contains(content, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        return $"{title}。{content}";
    }

    private static string InferThemeText(string content)
    {
        var semanticText = InferSemanticShortText(content);
        if (!string.IsNullOrWhiteSpace(semanticText))
        {
            return semanticText;
        }

        return $"最近有个主题值得回看：{content}";
    }

    private static string InferPreferenceText(string content)
    {
        var semanticText = InferSemanticShortText(content);
        if (!string.IsNullOrWhiteSpace(semanticText))
        {
            return semanticText;
        }

        return $"我注意到一个可能的偏好或边界：{content}";
    }

    private static string InferHabitText(string content)
    {
        var semanticText = InferSemanticShortText(content);
        if (!string.IsNullOrWhiteSpace(semanticText))
        {
            return semanticText;
        }

        return $"这可能是一个正在形成的习惯：{content}";
    }

    private static string InferGoalText(string content)
    {
        var semanticText = InferSemanticShortText(content);
        if (!string.IsNullOrWhiteSpace(semanticText))
        {
            return semanticText;
        }

        return $"这看起来像一个目标或计划：{content}";
    }

    private static string InferTemporaryContextText(string content)
    {
        var semanticText = InferSemanticShortText(content);
        if (!string.IsNullOrWhiteSpace(semanticText))
        {
            return semanticText;
        }

        return $"这可能是近期需要参考的背景：{content}";
    }

    private static string? InferSemanticShortText(string content)
    {
        if (ContainsAny(content, "codex", "lifeos", "项目", "整理"))
        {
            return "你最近在持续整理项目相关的事情。";
        }

        if (ContainsAny(content, "无人机", "西安", "国家版本馆", "飞行", "出行"))
        {
            return "你最近在记录出行和新体验。";
        }

        if (ContainsAny(content, "美食", "吃", "饺子", "蒸饺", "支出"))
        {
            return "你最近在记录饮食和消费感受。";
        }

        if (ContainsAny(content, "骑车", "骑行", "心率", "运动"))
        {
            return "你最近在关注运动状态和身体感受。";
        }

        return null;
    }

    private static string CleanText(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        cleaned = cleaned
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

        if (cleaned.Length <= MaxInsightTextLength)
        {
            return cleaned;
        }

        return cleaned[..MaxInsightTextLength].TrimEnd() + "...";
    }

    private static string NormalizeForDedupe(string text)
    {
        return Regex
            .Replace(text, @"[\s，。！？、,.!?~～：:；;""'“”‘’（）()\[\]【】]+", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record InsightSignal(
        string Key,
        string Kind,
        string Text,
        string SourceEventId,
        DateTime OccurredAt,
        double Score);
}
