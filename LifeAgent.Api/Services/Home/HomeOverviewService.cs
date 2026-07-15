using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.PersonalContext;
using System.Text.RegularExpressions;

namespace LifeAgent.Api.Services.Home;

public sealed class HomeOverviewService : IHomeOverviewService
{
    private const int RecentEventCount = 3;
    private const int MaxInsightSourceEvents = 20;
    private const int MaxVisibleReminders = 3;
    private const int MaxVisiblePlanSignals = 3;
    private const int MaxTodayFocusItems = 3;
    private static readonly HashSet<string> GenericMatchFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "一个", "事情", "今天", "最近", "近期", "计划", "目标", "希望", "需要", "关注", "继续", "准备", "相关", "记住",
        "状态", "个人", "背景", "记录", "整理", "反复", "出现", "内容"
    };

    private readonly IPersonalContextService _personalContextService;
    private readonly IMemoryInsightPreviewService _memoryInsightPreviewService;
    private readonly IMemoryReviewInboxPreviewService _memoryReviewInboxPreviewService;
    private readonly IMemoryReviewStateStore _memoryReviewStateStore;
    private readonly TimeProvider _timeProvider;

    public HomeOverviewService(
        IPersonalContextService personalContextService,
        IMemoryInsightPreviewService memoryInsightPreviewService,
        IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
        IMemoryReviewStateStore memoryReviewStateStore,
        TimeProvider? timeProvider = null)
    {
        _personalContextService = personalContextService;
        _memoryInsightPreviewService = memoryInsightPreviewService;
        _memoryReviewInboxPreviewService = memoryReviewInboxPreviewService;
        _memoryReviewStateStore = memoryReviewStateStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HomeOverviewData> BuildAsync(
        string userId,
        int limit = MaxInsightSourceEvents,
        string? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        var boundedLimit = Math.Clamp(limit, RecentEventCount, 50);
        var context = await _personalContextService.LoadAsync(userId, new PersonalContextRequest
        {
            MaxEvents = boundedLimit,
            MaxMemories = 20,
            MaxReminders = MaxVisibleReminders,
            MaxPlanSignals = 20,
            ClientTimeZone = timeZone
        }, cancellationToken);

        var insightPreview = _memoryInsightPreviewService.BuildPreview(userId, context.Events);
        var reviewPreview = _memoryReviewInboxPreviewService.BuildPreview(userId, context.Events);
        var states = await _memoryReviewStateStore.ListByCandidateIdsAsync(
            userId,
            reviewPreview.Candidates.Select(candidate => candidate.Id).ToArray());
        var keptCandidates = await _memoryReviewStateStore.ListKeptCandidatesAsync(userId);
        var reviewCandidates = MemoryReviewInboxStateProjection.AddMissingKeptCandidates(
            MemoryReviewInboxStateProjection.Apply(reviewPreview, states),
            keptCandidates);
        var reviewStatusCounts = CountReviewStatuses(reviewCandidates.Candidates);
        var insights = AddPlanSignalInsight(insightPreview.Insights, context.PlanSignalCount);
        var todayFocus = BuildTodayFocus(context, insightPreview.Insights, timeZone);
        var dailyBrief = BuildDailyBrief(context, insightPreview.Insights, reviewStatusCounts, timeZone);

        return new HomeOverviewData
        {
            RecentEvents = context.Events.Take(RecentEventCount).Select(ToTimelineEventDto).ToArray(),
            HasMoreRecentEvents = context.Events.Count > RecentEventCount,
            Insights = insights,
            MemoryReviewCandidateCount = reviewCandidates.Candidates.Count,
            MemoryReviewPendingCandidateCount = reviewStatusCounts.Pending,
            MemoryReviewKeptCandidateCount = reviewStatusCounts.Kept,
            MemoryReviewRememberedCandidateCount = reviewStatusCounts.Remembered,
            MemoryCount = context.ActiveMemoryCount,
            PendingReminderCount = context.PendingReminderCount,
            PendingReminders = context.PendingReminders.Take(MaxVisibleReminders).Select(ToReminderDto).ToArray(),
            LatestReminder = context.PendingReminders.FirstOrDefault() is { } reminder ? ToReminderDto(reminder) : null,
            PlanSignalCount = context.PlanSignalCount,
            PlanSignals = context.PlanSignals.Take(MaxVisiblePlanSignals).Select(ToPlanSignalDto).ToArray(),
            LatestPlanSignal = context.PlanSignals.FirstOrDefault() is { } planSignal ? ToPlanSignalDto(planSignal) : null,
            TodayFocus = todayFocus,
            DailyBrief = dailyBrief,
            ReadOnly = true,
            WroteData = false,
            Executed = false
        };
    }

    private static ReviewStatusCounts CountReviewStatuses(IReadOnlyList<MemoryReviewCandidateItem> candidates)
    {
        var pending = 0;
        var kept = 0;
        var remembered = 0;

        foreach (var candidate in candidates)
        {
            switch (candidate.ReviewStatus?.Trim().ToLowerInvariant())
            {
                case "kept":
                    kept++;
                    break;
                case "remembered":
                    remembered++;
                    break;
                case "pending":
                case "":
                case null:
                    pending++;
                    break;
            }
        }

        return new ReviewStatusCounts(pending, kept, remembered);
    }

    private HomeOverviewDailyBriefDto BuildDailyBrief(
        PersonalContextSnapshot context,
        IReadOnlyList<MemoryInsightPreviewItem> insights,
        ReviewStatusCounts reviewStatusCounts,
        string? timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        var candidates = new List<DailyBriefCandidate>();

        foreach (var reminder in context.PendingReminders)
        {
            var localDueAt = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(reminder.DueAt), timeZone);
            if (localDueAt < localNow)
            {
                candidates.Add(new DailyBriefCandidate(
                    ToBriefSignal(
                        $"reminder_{reminder.Id}",
                        "有提醒已经逾期。",
                        $"{reminder.Title} 已经过了时间，建议今天先处理。",
                        "due_reminder",
                        "/reminders"),
                    1000,
                    localDueAt));
            }
            else if (localDueAt.Date == localNow.Date)
            {
                candidates.Add(new DailyBriefCandidate(
                    ToBriefSignal(
                        $"reminder_{reminder.Id}",
                        "今天有提醒到期。",
                        $"{reminder.Title} 今天 {localDueAt:HH:mm} 到期。",
                        "due_reminder",
                        "/reminders"),
                    900,
                    localDueAt));
            }
        }

        foreach (var signal in context.PlanSignals)
        {
            var signalText = $"{signal.Title} {signal.Content}";
            var relatedMemory = context.Memories
                .Where(memory => HasMeaningfulOverlap(signalText, memory.Content))
                .OrderByDescending(memory => memory.Importance)
                .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
                .FirstOrDefault();

            if (relatedMemory is not null)
            {
                candidates.Add(new DailyBriefCandidate(
                    ToBriefSignal(
                        $"plan_memory_{signal.Id}",
                        "有计划和你的个人背景相关。",
                        $"{signal.Title}：{MemoryRelationReason(relatedMemory.Type).TrimEnd('。')}",
                        "memory_related_plan",
                        "/plans"),
                    800 + relatedMemory.Importance,
                    signal.CreatedAt));
                continue;
            }

            if (insights.Any(insight =>
                    insight.SourceEventIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2 &&
                    HasMeaningfulOverlap(signalText, insight.Text)))
            {
                candidates.Add(new DailyBriefCandidate(
                    ToBriefSignal(
                        $"plan_pattern_{signal.Id}",
                        "有计划连着最近反复出现的主题。",
                        $"{signal.Title} 和最近反复出现的记录有关。",
                        "recent_pattern",
                        "/plans"),
                    700,
                    signal.CreatedAt));
            }
        }

        foreach (var insight in insights.Where(insight =>
                     insight.SourceEventIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2))
        {
            candidates.Add(new DailyBriefCandidate(
                ToBriefSignal(
                    $"pattern_{insight.SourceEventIds.FirstOrDefault() ?? NormalizeText(insight.Text)}",
                    "最近有重复出现的主题。",
                    insight.Text,
                    "recent_pattern",
                    "/life/review"),
                650,
                utcNow));
        }

        if (reviewStatusCounts.Pending > 0)
        {
            candidates.Add(new DailyBriefCandidate(
                ToBriefSignal(
                    "memory_review_pending",
                    "有新的记忆线索待判断。",
                    $"有 {reviewStatusCounts.Pending} 条可能值得记住的事等你确认。",
                    "memory_review_pending",
                    "/memory/review"),
                500,
                utcNow));
        }

        var signals = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SortAt.Ticks)
            .ThenBy(candidate => candidate.Signal.Id, StringComparer.Ordinal)
            .GroupBy(candidate => candidate.Signal.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Signal)
            .Take(4)
            .ToArray();

        if (signals.Length == 0)
        {
            signals = new[]
            {
                ToBriefSignal(
                    "empty_context",
                    "继续记录后，我会帮你整理重点。",
                    "今天暂时没有足够的记录、提醒或记忆可整理。",
                    "empty_context",
                    "/")
            };
        }

        return new HomeOverviewDailyBriefDto
        {
            Summary = BuildDailyBriefSummary(signals),
            Signals = signals,
            ContextCounts = new HomeOverviewDailyBriefContextCountsDto
            {
                RecentEventCount = context.Events.Count,
                ActiveMemoryCount = context.ActiveMemoryCount,
                PendingReminderCount = context.PendingReminderCount,
                PlanSignalCount = context.PlanSignalCount
            },
            ReadOnly = true,
            WroteData = false,
            Executed = false
        };
    }

    private static HomeOverviewDailyBriefSignalDto ToBriefSignal(
        string id,
        string title,
        string detail,
        string basis,
        string href)
    {
        return new HomeOverviewDailyBriefSignalDto
        {
            Id = id,
            Title = title,
            Detail = detail,
            Basis = basis,
            Href = href
        };
    }

    private static string BuildDailyBriefSummary(IReadOnlyList<HomeOverviewDailyBriefSignalDto> signals)
    {
        var bases = signals.Select(signal => signal.Basis).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (bases.Contains("due_reminder"))
        {
            return "今天先看时间相关的提醒。";
        }

        if (bases.Contains("memory_related_plan"))
        {
            return "今天适合推进和个人背景相关的计划。";
        }

        if (bases.Contains("recent_pattern"))
        {
            return "最近有重复出现的主题，值得回头看一眼。";
        }

        if (bases.Contains("memory_review_pending"))
        {
            return "有新的记忆线索等你判断。";
        }

        return "今天暂时没有需要特别整理的事。";
    }

    private IReadOnlyList<HomeOverviewTodayFocusDto> BuildTodayFocus(
        PersonalContextSnapshot context,
        IReadOnlyList<MemoryInsightPreviewItem> insights,
        string? timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        var candidates = new List<TodayFocusCandidate>();

        foreach (var reminder in context.PendingReminders)
        {
            var localDueAt = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(reminder.DueAt), timeZone);
            if (localDueAt < localNow)
            {
                candidates.Add(new TodayFocusCandidate(
                    ToFocus(reminder.Id, "reminder", reminder.Title, "已逾期，建议今天优先处理。", "/reminders", "overdue"),
                    1000,
                    localDueAt));
            }
            else if (localDueAt.Date == localNow.Date)
            {
                candidates.Add(new TodayFocusCandidate(
                    ToFocus(reminder.Id, "reminder", reminder.Title, $"今天 {localDueAt:HH:mm} 到期。", "/reminders", "due_today"),
                    900,
                    localDueAt));
            }
            else if (localDueAt <= localNow.AddHours(48))
            {
                candidates.Add(new TodayFocusCandidate(
                    ToFocus(reminder.Id, "reminder", reminder.Title, "未来 48 小时内到期。", "/reminders", "due_soon"),
                    600,
                    localDueAt));
            }
        }

        foreach (var signal in context.PlanSignals)
        {
            var signalText = $"{signal.Title} {signal.Content}";
            var relatedMemory = context.Memories
                .Where(memory => HasMeaningfulOverlap(signalText, memory.Content))
                .OrderByDescending(memory => memory.Importance)
                .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
                .FirstOrDefault();

            if (relatedMemory is not null)
            {
                candidates.Add(new TodayFocusCandidate(
                    ToFocus(signal.Id, "plan", signal.Title, MemoryRelationReason(relatedMemory.Type), "/plans", "memory_related"),
                    800 + relatedMemory.Importance,
                    signal.CreatedAt));
                continue;
            }

            if (insights.Any(insight =>
                    insight.SourceEventIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2 &&
                    HasMeaningfulOverlap(signalText, insight.Text)))
            {
                candidates.Add(new TodayFocusCandidate(
                    ToFocus(signal.Id, "plan", signal.Title, "与最近反复出现的主题相关。", "/plans", "recent_pattern"),
                    700,
                    signal.CreatedAt));
            }
        }

        foreach (var insight in insights)
        {
            var relatedMemory = context.Memories
                .Where(memory => HasMeaningfulOverlap(insight.Text, memory.Content))
                .OrderByDescending(memory => memory.Importance)
                .FirstOrDefault();
            if (relatedMemory is null)
            {
                continue;
            }

            var idSuffix = insight.SourceEventIds.FirstOrDefault() ?? NormalizeText(insight.Text);
            candidates.Add(new TodayFocusCandidate(
                ToFocus($"insight_{idSuffix}", "insight", insight.Text, "与你记住的个人背景相呼应。", "/life/review", "memory_related"),
                500 + relatedMemory.Importance,
                relatedMemory.UpdatedAt ?? relatedMemory.CreatedAt));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Type == "reminder"
                ? candidate.SortAt.Ticks
                : -candidate.SortAt.Ticks)
            .ThenBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
            .GroupBy(candidate => $"{candidate.Item.Type}:{candidate.Item.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Item)
            .Take(MaxTodayFocusItems)
            .ToArray();
    }

    private static HomeOverviewTodayFocusDto ToFocus(
        string id,
        string type,
        string title,
        string reason,
        string href,
        string basis)
    {
        return new HomeOverviewTodayFocusDto
        {
            Id = id,
            Type = type,
            Title = title,
            Reason = reason,
            Href = href,
            Basis = basis
        };
    }

    private static bool HasMeaningfulOverlap(string left, string right)
    {
        var leftFragments = ExtractMatchFragments(left);
        var rightFragments = ExtractMatchFragments(right);
        return leftFragments.Overlaps(rightFragments);
    }

    private static string MemoryRelationReason(string memoryType)
    {
        return memoryType switch
        {
            "goal" => "与你记住的目标相关。",
            "temporary_context" => "与你记住的近期背景相关。",
            "preference" or "constraint" => "与你记住的偏好或边界相关。",
            "habit" or "routine" => "与你记住的习惯相关。",
            _ => "与你记住的个人背景相关。"
        };
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

    private static string NormalizeText(string text)
    {
        return Regex.Replace(text ?? string.Empty, @"[^A-Za-z0-9\u4e00-\u9fff]+", string.Empty)
            .ToLowerInvariant();
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

    private sealed record TodayFocusCandidate(HomeOverviewTodayFocusDto Item, double Score, DateTime SortAt);

    private sealed record DailyBriefCandidate(HomeOverviewDailyBriefSignalDto Signal, double Score, DateTime SortAt);

    private static IReadOnlyList<MemoryInsightPreviewItem> AddPlanSignalInsight(
        IReadOnlyList<MemoryInsightPreviewItem> insights,
        int planSignalCount)
    {
        if (planSignalCount <= 0)
        {
            return insights;
        }

        var planInsight = new MemoryInsightPreviewItem
        {
            Kind = "temporary_context",
            Text = $"你最近有 {planSignalCount} 个正在准备的计划。",
            Confidence = 0.8,
            SourceEventIds = Array.Empty<string>()
        };

        return new[] { planInsight }
            .Concat(insights)
            .Take(3)
            .ToArray();
    }

    private static TimelineEventDto ToTimelineEventDto(LifeEvent item)
    {
        return new TimelineEventDto
        {
            Id = item.Id,
            Type = item.Type,
            SchemaVersion = item.SchemaVersion,
            Title = item.Title,
            Content = item.Content,
            OccurredAt = item.OccurredAt.ToString("O"),
            CreatedAt = item.CreatedAt.ToString("O"),
            TimeZone = item.TimeZone,
            Tags = item.Tags,
            Importance = item.Importance,
            Source = item.Source,
            StructuredData = item.StructuredData,
            ExtractionConfidence = item.ExtractionConfidence,
            NeedsReview = item.NeedsReview
        };
    }

    private static HomeOverviewReminderDto ToReminderDto(Reminder reminder)
    {
        return new HomeOverviewReminderDto
        {
            Id = reminder.Id,
            Title = reminder.Title,
            Description = reminder.Description,
            DueAt = reminder.DueAt.ToString("O"),
            Timezone = reminder.Timezone,
            Status = reminder.Status
        };
    }

    private static HomeOverviewPlanSignalDto ToPlanSignalDto(PlanSignal signal)
    {
        return new HomeOverviewPlanSignalDto
        {
            Id = signal.Id,
            Kind = signal.Kind,
            Title = signal.Title,
            Content = signal.Content,
            CreatedAt = signal.CreatedAt.ToString("O")
        };
    }

    private sealed record ReviewStatusCounts(int Pending, int Kept, int Remembered);
}
