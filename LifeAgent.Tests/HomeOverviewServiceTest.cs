using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Home;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.PersonalContext;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class HomeOverviewServiceTest
{
    [Fact]
    public async Task BuildAsync_EmptyContext_ReturnsReadOnlyOverview()
    {
        var service = Service(Array.Empty<LifeEvent>(), new InMemoryMemoryRepository());

        var overview = await service.BuildAsync("user_a");

        Assert.Empty(overview.RecentEvents);
        Assert.Empty(overview.Insights);
        Assert.Equal(0, overview.MemoryReviewCandidateCount);
        Assert.Equal(0, overview.MemoryCount);
        Assert.Equal(0, overview.PendingReminderCount);
        Assert.Null(overview.LatestReminder);
        Assert.True(overview.ReadOnly);
        Assert.False(overview.WroteData);
        Assert.False(overview.Executed);
    }

    [Fact]
    public async Task BuildAsync_ReturnsRecentThreeEventsAndSignals()
    {
        var service = Service(
            new[]
            {
                Event("evt_old", "旧记录", "很早以前的记录。", minutesAgo: 50),
                Event("evt_project", "整理 LifeOS", "今天继续整理 LifeOS 项目。", minutesAgo: 30),
                Event("evt_xinjiang", "新疆计划", "下周应该就在去新疆的路上啦。", minutesAgo: 20),
                Event("evt_brand", "运动服饰", "种草了 kolon sports，版型好看，但是价格很贵。", minutesAgo: 10)
            },
            new InMemoryMemoryRepository());

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(new[] { "evt_brand", "evt_xinjiang", "evt_project" }, overview.RecentEvents.Select(item => item.Id));
        Assert.True(overview.HasMoreRecentEvents);
        Assert.Contains(overview.Insights, insight => insight.Text == "你近期有去新疆的出行计划。");
        Assert.True(overview.MemoryReviewCandidateCount > 0);
    }

    [Fact]
    public async Task BuildAsync_CountsActiveMemoriesAndPendingReminders()
    {
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我最近在关注运动服饰。",
            Importance = 3
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Archived.ToSnakeCaseString(),
            Content = "已归档内容。",
            Importance = 3
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "过期内容。",
            Importance = 3,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });

        var service = Service(
            Array.Empty<LifeEvent>(),
            memoryRepository,
            new[]
            {
                Reminder("rem_later", "稍后提醒", DateTime.UtcNow.AddHours(2), "pending"),
                Reminder("rem_done", "完成提醒", DateTime.UtcNow.AddHours(1), "completed"),
                Reminder("rem_soon", "最近提醒", DateTime.UtcNow.AddHours(1), "pending")
            });

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(1, overview.MemoryCount);
        Assert.Equal(2, overview.PendingReminderCount);
        Assert.NotNull(overview.LatestReminder);
        Assert.Equal("rem_soon", overview.LatestReminder!.Id);
    }

    [Fact]
    public async Task BuildAsync_IncludesPlanSignalCountAndLatestSignal()
    {
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            planSignals: new[]
            {
                PlanSignal("plan_old", "旧计划", minutesAgo: 20),
                PlanSignal("plan_new", "新计划", minutesAgo: 5)
            });

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(2, overview.PlanSignalCount);
        Assert.NotNull(overview.LatestPlanSignal);
        Assert.Equal("plan_new", overview.LatestPlanSignal!.Id);
    }

    private static HomeOverviewService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IReadOnlyList<Reminder>? reminders = null,
        IReadOnlyList<PlanSignal>? planSignals = null,
        IMemoryReviewStateStore? reviewStateStore = null)
    {
        return new HomeOverviewService(
            new PersonalContextService(
                new FakeLifeEventService(events),
                memoryRepository,
                new FakeReminderService(reminders ?? Array.Empty<Reminder>()),
                new FakePlanSignalService(planSignals ?? Array.Empty<PlanSignal>()),
                NullLogger<PersonalContextService>.Instance),
            new MemoryInsightPreviewService(new MemoryExtractionService(new MemoryProposalGuard())),
            new MemoryReviewInboxPreviewService(),
            reviewStateStore ?? new FakeMemoryReviewStateStore());
    }

    private static LifeEvent Event(string id, string title, string content, int minutesAgo)
    {
        var occurredAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        return new LifeEvent
        {
            Id = id,
            UserId = "user_a",
            Type = "life",
            Title = title,
            Content = content,
            CreatedAt = occurredAt,
            OccurredAt = occurredAt,
            Tags = new List<string> { "生活日常" },
            Importance = 1
        };
    }

    private static Reminder Reminder(string id, string title, DateTime dueAt, string status)
    {
        return new Reminder
        {
            Id = id,
            UserId = "user_a",
            Title = title,
            DueAt = dueAt,
            Timezone = "Asia/Shanghai",
            Status = status
        };
    }

    private static PlanSignal PlanSignal(string id, string title, int minutesAgo)
    {
        var createdAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        return new PlanSignal
        {
            Id = id,
            UserId = "user_a",
            Kind = "plan",
            Title = title,
            Content = title,
            Status = "active",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private sealed class FakeMemoryReviewStateStore : IMemoryReviewStateStore
    {
        public Task<IReadOnlyDictionary<string, MemoryReviewStateRecord>> ListByCandidateIdsAsync(
            string userId,
            IReadOnlyList<string> candidateIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, MemoryReviewStateRecord>>(
                new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase));
        }

        public Task<IReadOnlyList<MemoryReviewCandidateItem>> ListKeptCandidatesAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MemoryReviewCandidateItem>>(Array.Empty<MemoryReviewCandidateItem>());
        }

        public Task<MemoryReviewCandidateItem?> GetCandidateAsync(
            string userId,
            string candidateId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MemoryReviewCandidateItem?>(null);
        }

        public Task<MemoryReviewStateRecord> UpsertAsync(
            string userId,
            MemoryReviewStateUpsertRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("FakeMemoryReviewStateStore is read-only.");
        }
    }

}
