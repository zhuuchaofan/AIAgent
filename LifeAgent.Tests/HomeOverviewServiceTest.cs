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
        Assert.Empty(overview.ContextThreads);
        Assert.Equal(0, overview.MemoryReviewCandidateCount);
        Assert.Equal(0, overview.MemoryCount);
        Assert.Equal(0, overview.PendingReminderCount);
        Assert.Null(overview.LatestReminder);
        Assert.Empty(overview.TodayFocus);
        Assert.Equal("今天暂时没有需要特别整理的事。", overview.DailyBrief.Summary);
        var emptySignal = Assert.Single(overview.DailyBrief.Signals);
        Assert.Equal("empty_context", emptySignal.Basis);
        Assert.Equal("继续记录", emptySignal.ActionLabel);
        Assert.Contains("没有明确", emptySignal.Explanation, StringComparison.Ordinal);
        Assert.Equal(0, overview.DailyBrief.ContextCounts.RecentEventCount);
        Assert.Equal(0, overview.DailyBrief.ContextCounts.ActiveMemoryCount);
        Assert.True(overview.DailyBrief.ReadOnly);
        Assert.False(overview.DailyBrief.WroteData);
        Assert.False(overview.DailyBrief.Executed);
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
    public async Task BuildAsync_ReturnsMemoryReviewCountsByStatus()
    {
        var reviewedAt = DateTime.UtcNow;
        var reviewStateStore = new FakeMemoryReviewStateStore(new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["review_project_work"] = new("review_project_work", "kept", reviewedAt, reviewedAt),
            ["review_xinjiang_travel_plan"] = new("review_xinjiang_travel_plan", "remembered", reviewedAt, reviewedAt, "mem_xinjiang")
        });
        var service = Service(
            new[]
            {
                Event("evt_brand", "户外品牌", "种草了 kolon sports，版型好看，但是价格很贵。", minutesAgo: 20),
                Event("evt_project", "项目和出行", "最近让 codex 整理 LifeOS 项目。下周应该就在去新疆的路上啦。", minutesAgo: 10)
            },
            new InMemoryMemoryRepository(),
            reviewStateStore: reviewStateStore);

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(4, overview.MemoryReviewCandidateCount);
        Assert.Equal(2, overview.MemoryReviewPendingCandidateCount);
        Assert.Equal(1, overview.MemoryReviewKeptCandidateCount);
        Assert.Equal(1, overview.MemoryReviewRememberedCandidateCount);
        Assert.Contains(overview.DailyBrief.Signals, signal =>
            signal.Basis == "memory_review_pending" &&
            signal.Detail.Contains("2 条", StringComparison.Ordinal) &&
            signal.ActionLabel == "判断记忆" &&
            signal.Explanation.Contains("确认后才会进入长期记忆", StringComparison.Ordinal));
        Assert.True(overview.ReadOnly);
        Assert.False(overview.WroteData);
        Assert.False(overview.Executed);
    }

    [Fact]
    public async Task BuildAsync_DoesNotCountMemoryOverlappingCandidateAsPendingReview()
    {
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Habit.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我会关注运动状态和身体感受。",
            Importance = 4
        });
        var service = Service(
            new[]
            {
                Event("evt_bike", "骑车", "今天骑车回来，心率不高。", minutesAgo: 10)
            },
            memoryRepository);

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(1, overview.MemoryReviewCandidateCount);
        Assert.Equal(0, overview.MemoryReviewPendingCandidateCount);
        Assert.Equal(1, overview.MemoryReviewRememberedCandidateCount);
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
        Assert.Equal(new[] { "rem_soon", "rem_later" }, overview.PendingReminders.Select(item => item.Id));
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
        Assert.Equal(new[] { "plan_new", "plan_old" }, overview.PlanSignals.Select(item => item.Id));
        Assert.NotNull(overview.LatestPlanSignal);
        Assert.Equal("plan_new", overview.LatestPlanSignal!.Id);
    }

    [Fact]
    public async Task BuildAsync_LimitsDailyHubActionItemsToThree()
    {
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new[]
            {
                Reminder("rem_1", "提醒一", DateTime.UtcNow.AddHours(1), "pending"),
                Reminder("rem_2", "提醒二", DateTime.UtcNow.AddHours(2), "pending"),
                Reminder("rem_3", "提醒三", DateTime.UtcNow.AddHours(3), "pending"),
                Reminder("rem_4", "提醒四", DateTime.UtcNow.AddHours(4), "pending")
            },
            new[]
            {
                PlanSignal("plan_1", "计划一", minutesAgo: 1),
                PlanSignal("plan_2", "计划二", minutesAgo: 2),
                PlanSignal("plan_3", "计划三", minutesAgo: 3),
                PlanSignal("plan_4", "计划四", minutesAgo: 4)
            });

        var overview = await service.BuildAsync("user_a");

        Assert.Equal(4, overview.PendingReminderCount);
        Assert.Equal(3, overview.PendingReminders.Count);
        Assert.Equal(4, overview.PlanSignalCount);
        Assert.Equal(3, overview.PlanSignals.Count);
    }

    [Fact]
    public async Task BuildAsync_RanksOverdueTodayAndSoonRemindersByLocalTime()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new[]
            {
                Reminder("rem_soon", "明天提醒", now.UtcDateTime.AddHours(26), "pending"),
                Reminder("rem_today", "今天提醒", now.UtcDateTime.AddHours(2), "pending"),
                Reminder("rem_overdue", "逾期提醒", now.UtcDateTime.AddHours(-1), "pending")
            },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        Assert.Equal(
            new[] { "rem_overdue", "rem_today", "rem_soon" },
            overview.TodayFocus.Select(item => item.Id));
        Assert.Equal(
            new[] { "overdue", "due_today", "due_soon" },
            overview.TodayFocus.Select(item => item.Basis));
        Assert.Equal(100, overview.TodayFocus[0].Priority);
        Assert.Equal("最高", overview.TodayFocus[0].PriorityLabel);
        Assert.Equal("查看提醒", overview.TodayFocus[0].ActionLabel);
        Assert.Contains("已经超过时间", overview.TodayFocus[0].Explanation, StringComparison.Ordinal);
        Assert.Equal("今天先看时间相关的提醒。", overview.DailyBrief.Summary);
        Assert.Equal("due_reminder", overview.DailyBrief.Signals[0].Basis);
        Assert.Contains("逾期提醒", overview.DailyBrief.Signals[0].Detail, StringComparison.Ordinal);
        Assert.Equal("查看提醒", overview.DailyBrief.Signals[0].ActionLabel);
        Assert.Contains("最明确的待处理事项", overview.DailyBrief.Signals[0].Explanation, StringComparison.Ordinal);

        var thread = overview.ContextThreads.First();
        Assert.Equal("reminder_rem_overdue", thread.Id);
        Assert.Equal("temporary_context", thread.Kind);
        Assert.Equal(100, thread.Priority);
        Assert.Equal("/reminders", thread.Href);
        Assert.Equal("查看提醒", thread.ActionLabel);
        Assert.Contains("提醒", thread.Explanation, StringComparison.Ordinal);
        Assert.Contains(thread.Evidence, evidence => evidence.SourceType == "reminder" && evidence.SourceId == "rem_overdue");
    }

    [Fact]
    public async Task BuildAsync_IncludesOnlyMemoryRelatedUndatedPlanSignals()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Goal.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我计划去新疆旅行。",
            Importance = 5
        });
        var service = Service(
            Array.Empty<LifeEvent>(),
            memoryRepository,
            planSignals: new[]
            {
                PlanSignal("plan_related", "整理新疆路线", minutesAgo: 10),
                PlanSignal("plan_unrelated", "看看新的咖啡机", minutesAgo: 5)
            },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        var focus = Assert.Single(overview.TodayFocus);
        Assert.Equal("plan_related", focus.Id);
        Assert.Equal("plan", focus.Type);
        Assert.Equal("memory_related", focus.Basis);
        Assert.Equal("与你记住的目标相关。", focus.Reason);
        Assert.Equal(80, focus.Priority);
        Assert.Equal("相关", focus.PriorityLabel);
        Assert.Equal("查看计划", focus.ActionLabel);
        Assert.Contains("已记住的个人背景", focus.Explanation, StringComparison.Ordinal);
        Assert.Equal("今天适合推进和个人背景相关的计划。", overview.DailyBrief.Summary);
        Assert.Contains(overview.DailyBrief.Signals, signal =>
            signal.Basis == "memory_related_plan" &&
            signal.Detail.Contains("整理新疆路线", StringComparison.Ordinal) &&
            signal.ActionLabel == "查看计划" &&
            signal.Explanation.Contains("已记住的个人背景", StringComparison.Ordinal));

        var thread = Assert.Single(overview.ContextThreads);
        Assert.Equal("plan_memory_plan_related", thread.Id);
        Assert.Equal("goal", thread.Kind);
        Assert.Equal("整理新疆路线", thread.Title);
        Assert.Equal("/plans", thread.Href);
        Assert.Equal("查看计划", thread.ActionLabel);
        Assert.Contains("计划线索和已确认记忆", thread.Explanation, StringComparison.Ordinal);
        Assert.Contains(thread.Evidence, evidence => evidence.SourceType == "plan" && evidence.SourceId == "plan_related");
        Assert.Contains(thread.Evidence, evidence => evidence.SourceType == "memory");
    }

    [Fact]
    public async Task BuildAsync_AddsReadOnlyInsightOnlyWhenMemoryAndRecentPatternAgree()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Habit.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我最近关注运动状态和骑行习惯。",
            Importance = 4
        });
        var service = Service(
            new[]
            {
                Event("evt_ride", "骑行记录", "今天骑行时心率平稳。", minutesAgo: 5)
            },
            memoryRepository,
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        var focus = Assert.Single(overview.TodayFocus);
        Assert.Equal("insight", focus.Type);
        Assert.Equal("memory_related", focus.Basis);
        Assert.Equal("/life/review", focus.Href);
        Assert.Equal("查看回顾", focus.ActionLabel);
        Assert.Contains("复盘参考", focus.Explanation, StringComparison.Ordinal);
        Assert.True(overview.ReadOnly);
        Assert.False(overview.WroteData);
        Assert.False(overview.Executed);
    }

    [Fact]
    public async Task BuildAsync_IncludesPlanRelatedToRepeatedRecentPatternWithoutMemory()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var service = Service(
            new[]
            {
                Event("evt_project_1", "整理项目", "今天继续整理 LifeOS 项目。", minutesAgo: 20),
                Event("evt_project_2", "项目进展", "昨天也在推进项目整理。", minutesAgo: 40)
            },
            new InMemoryMemoryRepository(),
            planSignals: new[] { PlanSignal("plan_project", "完成 LifeOS 项目整理", minutesAgo: 5) },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        var focus = Assert.Single(overview.TodayFocus);
        Assert.Equal("plan_project", focus.Id);
        Assert.Equal("recent_pattern", focus.Basis);
        Assert.Equal("重复", focus.PriorityLabel);
        Assert.Equal("查看计划", focus.ActionLabel);
        Assert.Contains(overview.DailyBrief.Signals, signal =>
            signal.Basis == "recent_pattern" &&
            signal.ActionLabel == "查看计划");
    }

    [Fact]
    public async Task BuildAsync_DoesNotPromotePlanFromSingleRecentNoteWithoutMemory()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var service = Service(
            new[] { Event("evt_project", "整理项目", "今天整理了一次 LifeOS 项目。", minutesAgo: 20) },
            new InMemoryMemoryRepository(),
            planSignals: new[] { PlanSignal("plan_project", "完成 LifeOS 项目整理", minutesAgo: 5) },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        Assert.Empty(overview.TodayFocus);
        Assert.Empty(overview.ContextThreads);
        Assert.DoesNotContain(overview.DailyBrief.Signals, signal => signal.Basis == "recent_pattern");
    }

    [Fact]
    public async Task BuildAsync_AddsMemoryPatternThreadOnlyWithRepeatedRecentEvidence()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Habit.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我最近关注骑行状态和心率。",
            Importance = 4
        });
        var service = Service(
            new[]
            {
                Event("evt_ride_1", "骑行记录", "今天骑行心率比较平稳。", minutesAgo: 5),
                Event("evt_ride_2", "运动状态", "昨天骑行以后也关注了心率。", minutesAgo: 30)
            },
            memoryRepository,
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        var thread = Assert.Single(overview.ContextThreads);
        Assert.Equal("routine", thread.Kind);
        Assert.Equal("/life/review", thread.Href);
        Assert.Equal("查看回顾", thread.ActionLabel);
        Assert.Contains("至少两条近期记录", thread.Explanation, StringComparison.Ordinal);
        Assert.Contains(thread.Evidence, evidence => evidence.SourceType == "memory");
        Assert.Equal(2, thread.Evidence.Count(evidence => evidence.SourceType == "event"));
    }

    [Fact]
    public async Task BuildAsync_DoesNotUseExpiredTemporaryMemoryForContextThreads()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我正在准备新疆旅行。",
            Importance = 5,
            ExpiresAt = now.UtcDateTime.AddDays(-1)
        });
        var service = Service(
            Array.Empty<LifeEvent>(),
            memoryRepository,
            planSignals: new[] { PlanSignal("plan_xinjiang", "整理新疆路线", minutesAgo: 5) },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        Assert.Empty(overview.ContextThreads);
    }

    [Fact]
    public async Task BuildAsync_LimitsContextThreadsToThree()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Goal.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我计划去新疆旅行。",
            Importance = 5
        });
        var service = Service(
            Array.Empty<LifeEvent>(),
            memoryRepository,
            reminders: new[]
            {
                Reminder("rem_1", "提醒一", now.UtcDateTime.AddHours(-1), "pending"),
                Reminder("rem_2", "提醒二", now.UtcDateTime.AddHours(1), "pending"),
                Reminder("rem_3", "提醒三", now.UtcDateTime.AddHours(2), "pending")
            },
            planSignals: new[]
            {
                PlanSignal("plan_xinjiang_1", "整理新疆路线", minutesAgo: 5),
                PlanSignal("plan_xinjiang_2", "准备新疆行李", minutesAgo: 10)
            },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "Asia/Shanghai");

        Assert.Equal(3, overview.ContextThreads.Count);
        Assert.All(overview.ContextThreads, thread => Assert.NotEmpty(thread.Evidence));
    }

    [Fact]
    public async Task BuildAsync_InvalidTimeZoneFallsBackWithoutFailing()
    {
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new[] { Reminder("rem_today", "今天提醒", now.UtcDateTime.AddHours(2), "pending") },
            timeProvider: new FixedTimeProvider(now));

        var overview = await service.BuildAsync("user_a", timeZone: "not/a-time-zone");

        var focus = Assert.Single(overview.TodayFocus);
        Assert.Equal("due_today", focus.Basis);
    }

    private static HomeOverviewService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IReadOnlyList<Reminder>? reminders = null,
        IReadOnlyList<PlanSignal>? planSignals = null,
        IMemoryReviewStateStore? reviewStateStore = null,
        TimeProvider? timeProvider = null)
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
            reviewStateStore ?? new FakeMemoryReviewStateStore(),
            timeProvider: timeProvider);
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
        private readonly IReadOnlyDictionary<string, MemoryReviewStateRecord> _states;

        public FakeMemoryReviewStateStore()
            : this(new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase))
        {
        }

        public FakeMemoryReviewStateStore(IReadOnlyDictionary<string, MemoryReviewStateRecord> states)
        {
            _states = states;
        }

        public Task<IReadOnlyDictionary<string, MemoryReviewStateRecord>> ListByCandidateIdsAsync(
            string userId,
            IReadOnlyList<string> candidateIds,
            CancellationToken cancellationToken = default)
        {
            var requested = candidateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, MemoryReviewStateRecord>>(
                _states
                    .Where(pair => requested.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

}
