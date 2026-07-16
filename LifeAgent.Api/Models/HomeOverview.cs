using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Models;

public sealed class HomeOverviewResponse
{
    public bool Success { get; set; } = true;
    public HomeOverviewData Data { get; set; } = new();
}

public sealed class HomeOverviewData
{
    public IReadOnlyList<TimelineEventDto> RecentEvents { get; set; } = Array.Empty<TimelineEventDto>();
    public bool HasMoreRecentEvents { get; set; }
    public IReadOnlyList<MemoryInsightPreviewItem> Insights { get; set; } = Array.Empty<MemoryInsightPreviewItem>();
    public int MemoryReviewCandidateCount { get; set; }
    public int MemoryReviewPendingCandidateCount { get; set; }
    public int MemoryReviewKeptCandidateCount { get; set; }
    public int MemoryReviewRememberedCandidateCount { get; set; }
    public int MemoryCount { get; set; }
    public int PendingReminderCount { get; set; }
    public IReadOnlyList<HomeOverviewReminderDto> PendingReminders { get; set; } = Array.Empty<HomeOverviewReminderDto>();
    public HomeOverviewReminderDto? LatestReminder { get; set; }
    public int PlanSignalCount { get; set; }
    public IReadOnlyList<HomeOverviewPlanSignalDto> PlanSignals { get; set; } = Array.Empty<HomeOverviewPlanSignalDto>();
    public HomeOverviewPlanSignalDto? LatestPlanSignal { get; set; }
    public IReadOnlyList<HomeOverviewTodayFocusDto> TodayFocus { get; set; } = Array.Empty<HomeOverviewTodayFocusDto>();
    public HomeOverviewDailyBriefDto DailyBrief { get; set; } = new();
    public IReadOnlyList<HomeOverviewContextThreadDto> ContextThreads { get; set; } = Array.Empty<HomeOverviewContextThreadDto>();
    public HomeOverviewContextSpineDto ContextSpine { get; set; } = new();
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public sealed class HomeOverviewContextSpineDto
{
    public IReadOnlyList<HomeOverviewContextThreadDto> Threads { get; set; } = Array.Empty<HomeOverviewContextThreadDto>();
    public IReadOnlyList<HomeOverviewContextSpineSignalDto> Signals { get; set; } = Array.Empty<HomeOverviewContextSpineSignalDto>();
    public IReadOnlyList<HomeOverviewContextSpineLinkDto> NextBestLinks { get; set; } = Array.Empty<HomeOverviewContextSpineLinkDto>();
    public HomeOverviewDailyBriefContextCountsDto ContextCounts { get; set; } = new();
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public sealed class HomeOverviewContextSpineSignalDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public int Priority { get; set; }
}

public sealed class HomeOverviewContextSpineLinkDto
{
    public string Href { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class HomeOverviewDailyBriefDto
{
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<HomeOverviewDailyBriefSignalDto> Signals { get; set; } = Array.Empty<HomeOverviewDailyBriefSignalDto>();
    public HomeOverviewDailyBriefContextCountsDto ContextCounts { get; set; } = new();
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public sealed class HomeOverviewDailyBriefSignalDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Basis { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public sealed class HomeOverviewDailyBriefContextCountsDto
{
    public int RecentEventCount { get; set; }
    public int ActiveMemoryCount { get; set; }
    public int PendingReminderCount { get; set; }
    public int PlanSignalCount { get; set; }
}

public sealed class HomeOverviewTodayFocusDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string Basis { get; set; } = string.Empty;
    public string StatusGroup { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string PriorityLabel { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string FollowUpLabel { get; set; } = string.Empty;
    public string FollowUpHref { get; set; } = string.Empty;
    public string TrackingReason { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public sealed class HomeOverviewContextThreadDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Href { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public IReadOnlyList<HomeOverviewContextThreadEvidenceDto> Evidence { get; set; } = Array.Empty<HomeOverviewContextThreadEvidenceDto>();
}

public sealed class HomeOverviewContextThreadEvidenceDto
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
}

public sealed class HomeOverviewReminderDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DueAt { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class HomeOverviewPlanSignalDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
