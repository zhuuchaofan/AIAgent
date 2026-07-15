using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.PersonalContext;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class LifeReviewServiceTest
{
    [Fact]
    public async Task BuildReviewAsync_NoEventsOrMemories_ReturnsReadOnlyEmptyState()
    {
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new InspectableAnswerGenerator());

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.True(response.Success);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Equal(0, response.UsedEventCount);
        Assert.Equal(0, response.UsedMemoryCount);
        Assert.Equal(0, response.UsedPlanSignalCount);
        Assert.Single(response.Cards);
        Assert.Contains("记录多一点", response.Cards[0].Text);
        Assert.Empty(response.ReviewThemes);
        Assert.Empty(response.SourceEvents);
    }

    [Fact]
    public async Task BuildReviewAsync_WithJsonAnswer_ReturnsCardsAndEvidence()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "recent_state",
                      "text": "你最近在准备新疆出行，也持续记录运动感受。",
                      "sourceEventIds": ["evt_trip", "evt_bike", "evt_missing"]
                    },
                    {
                      "id": "repeated_themes",
                      "text": "骑行和身体状态最近多次出现。",
                      "sourceEventIds": ["evt_bike"]
                    }
                  ]
                }
                """
        };
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我近期有去新疆的出行计划。",
            Importance = 4,
            ExpiresAt = DateTime.UtcNow.AddDays(20)
        });

        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_trip",
                    UserId = "user_a",
                    Title = "准备新疆旅行",
                    Content = "下周要去新疆，火车票已经买好。",
                    Tags = new List<string> { "旅行" },
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                },
                new LifeEvent
                {
                    Id = "evt_bike",
                    UserId = "user_a",
                    Title = "骑车回来",
                    Content = "今天骑车回来，心率不高。",
                    Tags = new List<string> { "运动" },
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    OccurredAt = DateTime.UtcNow.AddHours(-2)
                }
            },
            memoryRepository,
            answerGenerator);

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest
        {
            ClientTimeZone = "Asia/Shanghai"
        });

        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Equal(2, response.UsedEventCount);
        Assert.Equal(1, response.UsedMemoryCount);
        Assert.Equal(0, response.UsedPlanSignalCount);
        Assert.Equal(2, response.Cards.Count);
        Assert.Equal("最近状态", response.Cards[0].Title);
        Assert.Equal(new[] { "evt_trip", "evt_bike" }, response.Cards[0].SourceEventIds);
        Assert.DoesNotContain("evt_missing", response.Cards[0].SourceEventIds);
        Assert.Equal(2, response.SourceEvents.Count);
        Assert.Contains("只读生活回顾整理器", answerGenerator.LastSystemInstruction);
        Assert.Contains("sourceEventIds 只能引用输入中存在的 event id", answerGenerator.LastSystemInstruction);
        Assert.Contains("id: evt_trip", answerGenerator.LastUserPrompt);
        Assert.Contains("我近期有去新疆的出行计划。", answerGenerator.LastUserPrompt);
    }

    [Fact]
    public async Task BuildReviewAsync_IncludesPlanSignalsInPromptAndCountsContext()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "upcoming_plans",
                      "text": "你近期有去新疆的计划线索。",
                      "sourceEventIds": []
                    }
                  ]
                }
                """
        };

        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            answerGenerator,
            new[]
            {
                new PlanSignal
                {
                    Id = "plan_1",
                    UserId = "user_a",
                    Kind = "trip",
                    Title = "新疆出行",
                    Content = "下周可能在去新疆的路上。",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow.AddHours(-1)
                }
            });

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.Equal(0, response.UsedEventCount);
        Assert.Equal(1, response.UsedPlanSignalCount);
        Assert.Single(response.Cards);
        Assert.Equal("近期计划", response.Cards[0].Title);
        Assert.Contains("新疆", response.Cards[0].Text);
        Assert.Contains("计划线索", answerGenerator.LastUserPrompt);
        Assert.Contains("新疆出行", answerGenerator.LastUserPrompt);
        Assert.Contains("可以只读地引用计划线索", answerGenerator.LastSystemInstruction);
    }

    [Fact]
    public async Task BuildReviewAsync_GeneratorFails_ReturnsFallbackWithoutThrowing()
    {
        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_1",
                    UserId = "user_a",
                    Title = "整理 LifeOS 项目",
                    Content = "最近在整理项目。",
                    CreatedAt = DateTime.UtcNow,
                    OccurredAt = DateTime.UtcNow
                }
            },
            new InMemoryMemoryRepository(),
            new FailingAnswerGenerator());

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.True(response.Success);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Equal(1, response.UsedEventCount);
        Assert.Equal(0, response.UsedPlanSignalCount);
        Assert.Equal(4, response.Cards.Count);
        Assert.Contains("整理 LifeOS 项目", response.Cards[0].Text);
        Assert.Equal(new[] { "evt_1" }, response.Cards[0].SourceEventIds);
    }

    [Fact]
    public async Task BuildReviewAsync_TodayPeriodOnlyUsesTodayEvents()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "recent_state",
                      "text": "今天主要记录了骑车状态。",
                      "sourceEventIds": ["evt_today", "evt_old"]
                    }
                  ]
                }
                """
        };
        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_today",
                    UserId = "user_a",
                    Title = "今天骑车",
                    Content = "今天骑车回来，心率不高。",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                    OccurredAt = DateTime.UtcNow.AddMinutes(-30)
                },
                new LifeEvent
                {
                    Id = "evt_old",
                    UserId = "user_a",
                    Title = "很久以前的记录",
                    Content = "这条不应该进入今天回顾。",
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    OccurredAt = DateTime.UtcNow.AddDays(-10)
                }
            },
            new InMemoryMemoryRepository(),
            answerGenerator);

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest
        {
            Period = "today",
            ClientTimeZone = "UTC"
        });

        Assert.Equal("today", response.Period);
        Assert.Equal("今天", response.WindowLabel);
        Assert.Equal(1, response.UsedEventCount);
        Assert.Equal(new[] { "evt_today" }, response.Cards[0].SourceEventIds);
        Assert.DoesNotContain("evt_old", answerGenerator.LastUserPrompt);
    }

    [Fact]
    public async Task BuildReviewAsync_AddsMemoryEvidenceHintsForRelatedCards()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "recent_state",
                      "text": "骑行和身体状态最近值得回看。",
                      "sourceEventIds": ["evt_bike"]
                    }
                  ]
                }
                """
        };
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
                new LifeEvent
                {
                    Id = "evt_bike",
                    UserId = "user_a",
                    Title = "骑车回来",
                    Content = "今天骑车回来，心率不高，身体感觉还不错。",
                    Tags = new List<string> { "运动" },
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                }
            },
            memoryRepository,
            answerGenerator);

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        var hint = Assert.Single(response.Cards[0].EvidenceHints);
        Assert.Equal("memory", hint.Kind);
        Assert.Equal("关联记忆", hint.Label);
        Assert.Contains("运动状态", hint.Text, StringComparison.Ordinal);
        Assert.Equal("/memory", hint.Href);
        Assert.Contains("习惯", hint.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildReviewAsync_AddsPlanSignalEvidenceWhenNoMemoryMatches()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "upcoming_plans",
                      "text": "新疆出行计划最近需要继续整理。",
                      "sourceEventIds": []
                    }
                  ]
                }
                """
        };
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            answerGenerator,
            new[]
            {
                new PlanSignal
                {
                    Id = "plan_xinjiang",
                    UserId = "user_a",
                    Kind = "trip",
                    Title = "新疆出行路线",
                    Content = "下周去新疆，继续整理路线。",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow.AddHours(-1)
                }
            });

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        var hint = Assert.Single(response.Cards[0].EvidenceHints);
        Assert.Equal("plan_signal", hint.Kind);
        Assert.Equal("相关计划", hint.Label);
        Assert.Contains("新疆出行路线", hint.Text, StringComparison.Ordinal);
        Assert.Equal("/plans", hint.Href);
        Assert.Contains("出行计划", hint.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildReviewAsync_IgnoresArchivedOrExpiredMemoryEvidenceAndLimitsHints()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = """
                {
                  "cards": [
                    {
                      "id": "recent_state",
                      "text": "新疆出行和运动状态最近都值得回看。",
                      "sourceEventIds": ["evt_mix"]
                    }
                  ]
                }
                """
        };
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Goal.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我计划去新疆旅行。",
            Importance = 5
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Habit.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我会关注运动状态和身体感受。",
            Importance = 4
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Archived.ToSnakeCaseString(),
            Content = "归档的新疆偏好不应出现。",
            Importance = 5
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "过期的新疆背景不应出现。",
            Importance = 5,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });

        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_mix",
                    UserId = "user_a",
                    Title = "整理新疆出行和运动状态",
                    Content = "今天整理新疆路线，也记录了运动状态。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                }
            },
            memoryRepository,
            answerGenerator,
            new[]
            {
                new PlanSignal
                {
                    Id = "plan_xinjiang",
                    UserId = "user_a",
                    Kind = "trip",
                    Title = "新疆出行路线",
                    Content = "继续整理新疆路线。",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            });

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.Equal(2, response.Cards[0].EvidenceHints.Count);
        Assert.All(response.Cards[0].EvidenceHints, hint => Assert.Equal("memory", hint.Kind));
        Assert.DoesNotContain(response.Cards[0].EvidenceHints, hint => hint.Text.Contains("归档", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Cards[0].EvidenceHints, hint => hint.Text.Contains("过期", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildReviewAsync_AddsMemoryProgressThemeWhenConfirmedMemoryRepeatsAcrossEvents()
    {
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Habit.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我会持续关注运动状态和身体感受。",
            Importance = 4
        });

        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_run",
                    UserId = "user_a",
                    Title = "跑步后的运动状态",
                    Content = "今天记录了运动状态和心率。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                },
                new LifeEvent
                {
                    Id = "evt_body",
                    UserId = "user_a",
                    Title = "身体感受记录",
                    Content = "晚上补了一条身体感受，恢复还可以。",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    OccurredAt = DateTime.UtcNow.AddHours(-2)
                }
            },
            memoryRepository,
            new FailingAnswerGenerator());

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        var theme = Assert.Single(response.ReviewThemes);
        Assert.Equal("memory_progress", theme.Kind);
        Assert.Contains("运动状态", theme.Title, StringComparison.Ordinal);
        Assert.Equal("/memory", theme.Href);
        Assert.Equal("查看记忆", theme.ActionLabel);
        var hint = Assert.Single(theme.EvidenceHints);
        Assert.Equal("memory", hint.Kind);
        Assert.Contains("习惯", hint.Reason, StringComparison.Ordinal);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
    }

    [Fact]
    public async Task BuildReviewAsync_AddsPlanProgressThemeWhenPlanMatchesConfirmedMemory()
    {
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我近期有新疆出行计划。",
            Importance = 4,
            ExpiresAt = DateTime.UtcNow.AddDays(20)
        });

        var service = Service(
            Array.Empty<LifeEvent>(),
            memoryRepository,
            new FailingAnswerGenerator(),
            new[]
            {
                new PlanSignal
                {
                    Id = "plan_xinjiang",
                    UserId = "user_a",
                    Kind = "trip",
                    Title = "新疆出行路线",
                    Content = "继续整理新疆出行路线。",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow.AddHours(-1)
                }
            });

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        var theme = Assert.Single(response.ReviewThemes);
        Assert.Equal("plan_progress", theme.Kind);
        Assert.Contains("新疆出行路线", theme.Title, StringComparison.Ordinal);
        Assert.Equal("/plans", theme.Href);
        Assert.Equal("查看计划", theme.ActionLabel);
        Assert.Collection(
            theme.EvidenceHints,
            hint => Assert.Equal("plan_signal", hint.Kind),
            hint => Assert.Equal("memory", hint.Kind));
    }

    [Fact]
    public async Task BuildReviewAsync_DoesNotAddReviewThemeForSingleWeakPlanMention()
    {
        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_project",
                    UserId = "user_a",
                    Title = "处理项目安排",
                    Content = "今天只是处理了一下项目安排。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                }
            },
            new InMemoryMemoryRepository(),
            new FailingAnswerGenerator(),
            new[]
            {
                new PlanSignal
                {
                    Id = "plan_project",
                    UserId = "user_a",
                    Kind = "task",
                    Title = "项目安排",
                    Content = "项目安排需要看看。",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2)
                }
            });

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.Empty(response.ReviewThemes);
    }

    [Fact]
    public async Task BuildReviewAsync_DoesNotAddReviewThemeForExpiredTemporaryMemory()
    {
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我近期在准备新疆出行。",
            Importance = 5,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });

        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_1",
                    UserId = "user_a",
                    Title = "新疆出行路线",
                    Content = "今天整理新疆出行路线。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                },
                new LifeEvent
                {
                    Id = "evt_2",
                    UserId = "user_a",
                    Title = "新疆行李",
                    Content = "继续准备新疆出行行李。",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    OccurredAt = DateTime.UtcNow.AddHours(-2)
                }
            },
            memoryRepository,
            new FailingAnswerGenerator());

        var response = await service.BuildReviewAsync("user_a", new LifeReviewRequest());

        Assert.Empty(response.ReviewThemes);
    }

    private static LifeReviewService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IRagAnswerGenerator answerGenerator,
        IReadOnlyList<PlanSignal>? planSignals = null)
    {
        return new LifeReviewService(
            new PersonalContextService(
                new FakeLifeEventService(events),
                memoryRepository,
                new FakeReminderService(Array.Empty<Reminder>()),
                new FakePlanSignalService(planSignals ?? Array.Empty<PlanSignal>()),
                NullLogger<PersonalContextService>.Instance),
            answerGenerator,
            NullLogger<LifeReviewService>.Instance);
    }
}
