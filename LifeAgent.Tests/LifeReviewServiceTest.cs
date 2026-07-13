using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Memories;
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
        Assert.Single(response.Cards);
        Assert.Contains("记录多一点", response.Cards[0].Text);
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
        Assert.Equal(4, response.Cards.Count);
        Assert.Contains("整理 LifeOS 项目", response.Cards[0].Text);
        Assert.Equal(new[] { "evt_1" }, response.Cards[0].SourceEventIds);
    }

    private static LifeReviewService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IRagAnswerGenerator answerGenerator)
    {
        return new LifeReviewService(
            new FakeLifeEventService(events),
            memoryRepository,
            answerGenerator,
            NullLogger<LifeReviewService>.Instance);
    }
}
