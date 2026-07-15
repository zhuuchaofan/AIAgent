using System.Text.RegularExpressions;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.PersonalContext;

namespace LifeAgent.Api.Services.Home;

public sealed class PersonalContextThreadService : IPersonalContextThreadService
{
    private const int MaxThreads = 3;
    private const int MaxEvidence = 3;
    private static readonly HashSet<string> GenericMatchFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "一个", "事情", "今天", "最近", "近期", "计划", "目标", "希望", "需要", "关注", "继续", "准备", "相关", "记住",
        "状态", "个人", "背景", "记录", "整理", "反复", "出现", "内容", "这个", "那个", "可以", "已经", "自己"
    };

    private readonly TimeProvider _timeProvider;

    public PersonalContextThreadService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<HomeOverviewContextThreadDto> BuildThreads(
        PersonalContextSnapshot context,
        string? timeZoneId = null)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        var candidates = new List<ThreadCandidate>();

        AddReminderThreads(context, timeZone, localNow, candidates);
        AddMemoryRelatedPlanThreads(context, candidates);
        AddMemoryPatternThreads(context, candidates);

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.SortAt)
            .GroupBy(candidate => candidate.Thread.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Thread)
            .Take(MaxThreads)
            .ToArray();
    }

    private static void AddReminderThreads(
        PersonalContextSnapshot context,
        TimeZoneInfo timeZone,
        DateTime localNow,
        List<ThreadCandidate> candidates)
    {
        foreach (var reminder in context.PendingReminders)
        {
            var localDueAt = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(reminder.DueAt), timeZone);
            var isOverdue = localDueAt < localNow;
            var isDueToday = localDueAt.Date == localNow.Date;
            if (!isOverdue && !isDueToday)
            {
                continue;
            }

            candidates.Add(new ThreadCandidate(
                new HomeOverviewContextThreadDto
                {
                    Id = $"reminder_{reminder.Id}",
                    Kind = "temporary_context",
                    Title = reminder.Title,
                    Summary = isOverdue
                        ? "这件事已经过了提醒时间，今天需要先处理。"
                        : $"这件事今天 {localDueAt:HH:mm} 到期。",
                    Priority = isOverdue ? 100 : 90,
                    Href = "/reminders",
                    ActionLabel = "查看提醒",
                    Explanation = "这条主线来自明确时间的提醒，不会触发通知或自动执行。",
                    Evidence = new[]
                    {
                        Evidence("reminder", reminder.Id, reminder.Title, isOverdue ? "已逾期" : $"今天 {localDueAt:HH:mm} 到期", "/reminders")
                    }
                },
                isOverdue ? 1000 : 900,
                reminder.DueAt));
        }
    }

    private static void AddMemoryRelatedPlanThreads(
        PersonalContextSnapshot context,
        List<ThreadCandidate> candidates)
    {
        foreach (var signal in context.PlanSignals)
        {
            var signalText = $"{signal.Title} {signal.Content}";
            var relatedMemory = context.Memories
                .Where(memory => HasMeaningfulOverlap(signalText, memory.Content))
                .OrderByDescending(memory => memory.Importance)
                .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
                .FirstOrDefault();

            if (relatedMemory is null)
            {
                continue;
            }

            candidates.Add(new ThreadCandidate(
                new HomeOverviewContextThreadDto
                {
                    Id = $"plan_memory_{signal.Id}",
                    Kind = ToThreadKind(relatedMemory.Type),
                    Title = signal.Title,
                    Summary = $"{signal.Title} 和已记住的个人背景有关。",
                    Priority = 80 + relatedMemory.Importance,
                    Href = "/plans",
                    ActionLabel = "查看计划",
                    Explanation = "这条主线由计划线索和已确认记忆共同支持，只用于整理优先级。",
                    Evidence = new[]
                    {
                        Evidence("plan", signal.Id, signal.Title, signal.Content, "/plans"),
                        Evidence("memory", relatedMemory.Id, MemoryTypeLabel(relatedMemory.Type), relatedMemory.Content, "/memory")
                    }
                },
                800 + relatedMemory.Importance,
                signal.CreatedAt));
        }
    }

    private static void AddMemoryPatternThreads(
        PersonalContextSnapshot context,
        List<ThreadCandidate> candidates)
    {
        foreach (var memory in context.Memories.OrderByDescending(memory => memory.Importance))
        {
            var relatedEvents = context.Events
                .Where(item => HasMeaningfulOverlap($"{item.Title} {item.Content}", memory.Content))
                .OrderByDescending(GetOccurredAt)
                .Take(MaxEvidence)
                .ToArray();

            if (relatedEvents.Length < 2)
            {
                continue;
            }

            candidates.Add(new ThreadCandidate(
                new HomeOverviewContextThreadDto
                {
                    Id = $"memory_pattern_{memory.Id}",
                    Kind = ToThreadKind(memory.Type),
                    Title = MemoryTypeLabel(memory.Type),
                    Summary = "最近多条记录都和这条已记住的背景有关。",
                    Priority = 60 + memory.Importance,
                    Href = "/life/review",
                    ActionLabel = "查看回顾",
                    Explanation = "这条主线需要至少两条近期记录和已确认记忆互相印证，避免把一次性事件当作长期趋势。",
                    Evidence = new[]
                    {
                        Evidence("memory", memory.Id, MemoryTypeLabel(memory.Type), memory.Content, "/memory")
                    }
                    .Concat(relatedEvents.Select(item => Evidence("event", item.Id, item.Title, item.Content, "/life/review")))
                    .Take(MaxEvidence)
                    .ToArray()
                },
                600 + memory.Importance,
                relatedEvents.Max(GetOccurredAt)));
        }
    }

    private static HomeOverviewContextThreadEvidenceDto Evidence(
        string sourceType,
        string sourceId,
        string title,
        string detail,
        string href)
    {
        return new HomeOverviewContextThreadEvidenceDto
        {
            SourceType = sourceType,
            SourceId = sourceId,
            Title = string.IsNullOrWhiteSpace(title) ? "未命名依据" : title.Trim(),
            Detail = TrimDetail(detail),
            Href = href
        };
    }

    private static string ToThreadKind(string memoryType)
    {
        return memoryType switch
        {
            "goal" => "goal",
            "habit" or "routine" => "routine",
            "temporary_context" => "temporary_context",
            _ => "theme"
        };
    }

    private static string MemoryTypeLabel(string memoryType)
    {
        return memoryType switch
        {
            "goal" => "已记住的目标",
            "habit" or "routine" => "已记住的习惯",
            "temporary_context" => "已记住的近期背景",
            "preference" => "已记住的偏好",
            "constraint" => "已记住的边界",
            _ => "已记住的个人背景"
        };
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

    private static string TrimDetail(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= 80 ? text : $"{text[..80]}…";
    }

    private static DateTime GetOccurredAt(LifeEvent item)
    {
        return item.OccurredAt == default ? item.CreatedAt : item.OccurredAt;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Shanghai" : timeZoneId.Trim());
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private sealed record ThreadCandidate(HomeOverviewContextThreadDto Thread, double Score, DateTime SortAt);
}
