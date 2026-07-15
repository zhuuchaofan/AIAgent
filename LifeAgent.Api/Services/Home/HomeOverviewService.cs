using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.PersonalContext;

namespace LifeAgent.Api.Services.Home;

public sealed class HomeOverviewService : IHomeOverviewService
{
    private const int RecentEventCount = 3;
    private const int MaxInsightSourceEvents = 20;
    private const int MaxVisibleReminders = 1;

    private readonly IPersonalContextService _personalContextService;
    private readonly IMemoryInsightPreviewService _memoryInsightPreviewService;
    private readonly IMemoryReviewInboxPreviewService _memoryReviewInboxPreviewService;
    private readonly IMemoryReviewStateStore _memoryReviewStateStore;

    public HomeOverviewService(
        IPersonalContextService personalContextService,
        IMemoryInsightPreviewService memoryInsightPreviewService,
        IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
        IMemoryReviewStateStore memoryReviewStateStore)
    {
        _personalContextService = personalContextService;
        _memoryInsightPreviewService = memoryInsightPreviewService;
        _memoryReviewInboxPreviewService = memoryReviewInboxPreviewService;
        _memoryReviewStateStore = memoryReviewStateStore;
    }

    public async Task<HomeOverviewData> BuildAsync(
        string userId,
        int limit = MaxInsightSourceEvents,
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
            MaxReminders = MaxVisibleReminders
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

        return new HomeOverviewData
        {
            RecentEvents = context.Events.Take(RecentEventCount).Select(ToTimelineEventDto).ToArray(),
            HasMoreRecentEvents = context.Events.Count > RecentEventCount,
            Insights = insightPreview.Insights,
            MemoryReviewCandidateCount = reviewCandidates.Candidates.Count,
            MemoryCount = context.ActiveMemoryCount,
            PendingReminderCount = context.PendingReminderCount,
            LatestReminder = context.PendingReminders.FirstOrDefault() is { } reminder ? ToReminderDto(reminder) : null,
            ReadOnly = true,
            WroteData = false,
            Executed = false
        };
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
}
