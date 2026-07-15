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
    public int MemoryCount { get; set; }
    public int PendingReminderCount { get; set; }
    public HomeOverviewReminderDto? LatestReminder { get; set; }
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
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
