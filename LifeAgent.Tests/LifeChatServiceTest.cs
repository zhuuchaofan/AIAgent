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
        Assert.Equal(0, response.UsedReminderCount);
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
        Assert.Equal(0, response.UsedReminderCount);
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
    public async Task AnswerAsync_IncludesPendingRemindersAsReadOnlyContext()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = "明天要先处理键盘轴体更换。"
        };
        var service = Service(
            new[]
            {
                new LifeEvent
                {
                    Id = "evt_1",
                    UserId = "user_a",
                    Title = "晚上想到一个待办",
                    Content = "提醒我明天晚上九点找键盘轴体。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                }
            },
            new InMemoryMemoryRepository(),
            answerGenerator,
            new[]
            {
                new Reminder
                {
                    Id = "rem_1",
                    UserId = "user_a",
                    Title = "寻找键盘轴体",
                    Description = "为明天去公司更换做准备",
                    DueAt = new DateTime(2026, 7, 14, 13, 0, 0, DateTimeKind.Utc),
                    Timezone = "Asia/Shanghai",
                    Status = "pending"
                },
                new Reminder
                {
                    Id = "rem_2",
                    UserId = "user_a",
                    Title = "已完成提醒不应进入上下文",
                    DueAt = new DateTime(2026, 7, 15, 1, 0, 0, DateTimeKind.Utc),
                    Timezone = "Asia/Shanghai",
                    Status = "completed"
                }
            });

        var response = await service.AnswerAsync("user_a", new LifeChatRequest
        {
            Message = "明天有什么要做？",
            ClientTimeZone = "Asia/Shanghai"
        });

        Assert.Equal("明天要先处理键盘轴体更换。", response.Response);
        Assert.Equal(1, response.UsedReminderCount);
        Assert.Contains("待处理提醒", answerGenerator.LastUserPrompt);
        Assert.Contains("寻找键盘轴体", answerGenerator.LastUserPrompt);
        Assert.Contains("为明天去公司更换做准备", answerGenerator.LastUserPrompt);
        Assert.DoesNotContain("已完成提醒不应进入上下文", answerGenerator.LastUserPrompt);
        Assert.Contains("可以只读地引用待处理提醒", answerGenerator.LastSystemInstruction);
        Assert.Contains("不要创建、修改、完成或取消提醒", answerGenerator.LastSystemInstruction);
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
        Assert.Equal(0, response.UsedReminderCount);
        Assert.Contains("整理 LifeOS 项目", response.Response);
    }

    [Fact]
    public async Task AnswerAsync_IgnoresExpiredAndArchivedMemories()
    {
        var answerGenerator = new InspectableAnswerGenerator
        {
            AnswerToReturn = "最近主要是在整理项目。"
        };
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我喜欢骑行。",
            Importance = 3,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Archived.ToSnakeCaseString(),
            Content = "这条已经忘记。",
            Importance = 3
        });
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我最近在整理 LifeOS 项目。",
            Importance = 4,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

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
            memoryRepository,
            answerGenerator);

        var response = await service.AnswerAsync("user_a", new LifeChatRequest
        {
            Message = "我最近在做什么？"
        });

        Assert.Equal(1, response.UsedMemoryCount);
        Assert.DoesNotContain("我喜欢骑行。", answerGenerator.LastUserPrompt);
        Assert.DoesNotContain("这条已经忘记。", answerGenerator.LastUserPrompt);
        Assert.Contains("我最近在整理 LifeOS 项目。", answerGenerator.LastUserPrompt);
    }

    private static LifeChatService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IRagAnswerGenerator answerGenerator,
        IReadOnlyList<Reminder>? reminders = null)
    {
        return new LifeChatService(
            new FakeLifeEventService(events),
            memoryRepository,
            new FakeReminderService(reminders ?? Array.Empty<Reminder>()),
            answerGenerator,
            NullLogger<LifeChatService>.Instance);
    }
}
