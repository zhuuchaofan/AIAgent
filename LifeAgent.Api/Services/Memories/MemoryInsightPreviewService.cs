using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryInsightPreviewService : IMemoryInsightPreviewService
{
    private const int MaxInsightCount = 3;
    private const int MaxInsightTextLength = 72;
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

        return new MemoryInsightPreviewData
        {
            ScannedCount = events.Count,
            PreviewOnly = true,
            WroteData = false,
            MemoryWriteEnabled = false,
            Insights = insights
        };
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
            "preference" => $"我注意到一个可能的偏好或边界：{content}",
            "habit" => $"这可能是一个正在形成的习惯：{content}",
            "goal" => $"这看起来像一个目标或计划：{content}",
            "temporary_context" => $"这可能是近期需要参考的背景：{content}",
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

        return $"最近有个主题值得回看：{content}";
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
}
