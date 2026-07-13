using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryInsightPreviewService : IMemoryInsightPreviewService
{
    private const int MaxInsightCount = 3;
    private const int MaxInsightTextLength = 120;
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
                Title = e.Title,
                Content = e.Content,
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
            .GroupBy(insight => insight.Text, StringComparer.OrdinalIgnoreCase)
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
            _ => $"最近反复出现的主题：{content}"
        };
    }

    private static string CleanText(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        cleaned = cleaned.Replace("用户输入：", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

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
}
