using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Memories;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class LifeChatServiceTest
{
    [Fact]
    public async Task AnswerAsync_NoEventsOrMemories_ReturnsReadOnlyEmptyState()
    {
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new InspectableAnswerGenerator());

        var response = await service.AnswerAsync("user_a", new LifeChatRequest
        {
            Message = "我最近在关注什么？"
        });

        Assert.True(response.Success);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Equal(0, response.UsedEventCount);
        Assert.Equal(0, response.UsedMemoryCount);
        Assert.Contains("还没有足够", response.Response);
    }

    [Fact]
    public async Task AnswerAsync_WithLifeEventsAndMemories_BuildsPromptAndCountsContext()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = "你最近在关注运动状态，也在准备新疆出行。"
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
                    Id = "evt_1",
                    UserId = "user_a",
                    Title = "骑车回来",
                    Content = "今天骑车回来，心率不高。",
                    Tags = new List<string> { "运动", "身体" },
                    Importance = 3,
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    OccurredAt = DateTime.UtcNow.AddHours(-2)
                }
            },
            memoryRepository,
            answerGenerator);

        var response = await service.AnswerAsync("user_a", new LifeChatRequest
        {
            Message = "我最近状态怎么样？",
            ClientTimeZone = "Asia/Shanghai"
        });

        Assert.Equal("你最近在关注运动状态，也在准备新疆出行。", response.Response);
        Assert.Equal(1, response.UsedEventCount);
        Assert.Equal(1, response.UsedMemoryCount);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Contains("骑车回来", answerGenerator.LastUserPrompt);
        Assert.Contains("今天骑车回来，心率不高。", answerGenerator.LastUserPrompt);
        Assert.Contains("我近期有去新疆的出行计划。", answerGenerator.LastUserPrompt);
        Assert.Contains("只读生活问答助手", answerGenerator.LastSystemInstruction);
        Assert.Contains("2-3 条观察", answerGenerator.LastSystemInstruction);
        Assert.Contains("不超过 40-50 个中文字符", answerGenerator.LastSystemInstruction);
        Assert.Contains("先给结论，不主动展开日期、票务、设备、地点等证据细节", answerGenerator.LastSystemInstruction);
        Assert.Contains("禁止使用“以下是”“下面是”“你最近的状态”“具体来看”“以下是总结”“工作方面/休闲方面”等报告式开头或标题", answerGenerator.LastSystemInstruction);
    }

    [Fact]
    public async Task AnswerAsync_GeneratorFails_ReturnsFallbackWithoutThrowing()
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

        var response = await service.AnswerAsync("user_a", new LifeChatRequest
        {
            Message = "我最近在做什么？"
        });

        Assert.True(response.Success);
        Assert.True(response.ReadOnly);
        Assert.False(response.WroteData);
        Assert.False(response.Executed);
        Assert.Equal(1, response.UsedEventCount);
        Assert.Contains("整理 LifeOS 项目", response.Response);
    }

    private static LifeChatService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IRagAnswerGenerator answerGenerator)
    {
        return new LifeChatService(
            new FakeLifeEventService(events),
            memoryRepository,
            answerGenerator,
            NullLogger<LifeChatService>.Instance);
    }
}
