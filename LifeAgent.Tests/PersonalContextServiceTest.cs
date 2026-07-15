using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.PersonalContext;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class PersonalContextServiceTest
{
    [Fact]
    public async Task LoadAsync_FiltersDeletedEventsAndExpiredOrArchivedMemories()
    {
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
                Event("evt_1", "有效记录", DateTime.UtcNow.AddHours(-1)),
                Event("evt_deleted", "已删除记录", DateTime.UtcNow, isDeleted: true)
            },
            memoryRepository);

        var context = await service.LoadAsync("user_a", new PersonalContextRequest
        {
            MaxEvents = 10,
            MaxMemories = 10
        });

        Assert.Single(context.Events);
        Assert.Equal("evt_1", context.Events[0].Id);
        Assert.Single(context.Memories);
        Assert.Contains("整理 LifeOS", context.Memories[0].Content);
    }

    [Fact]
    public async Task LoadAsync_OnlyIncludesPendingRemindersOrderedByDueAt()
    {
        var service = Service(
            Array.Empty<LifeEvent>(),
            new InMemoryMemoryRepository(),
            new[]
            {
                Reminder("rem_late", "稍后的提醒", DateTime.UtcNow.AddHours(3), "pending"),
                Reminder("rem_done", "完成的提醒", DateTime.UtcNow.AddHours(1), "completed"),
                Reminder("rem_soon", "最近的提醒", DateTime.UtcNow.AddHours(1), "pending")
            });

        var context = await service.LoadAsync("user_a", new PersonalContextRequest
        {
            MaxReminders = 10
        });

        Assert.Equal(new[] { "rem_soon", "rem_late" }, context.PendingReminders.Select(item => item.Id));
    }

    [Fact]
    public async Task LoadAsync_TodayPeriodUsesClientTimeZone()
    {
        var service = Service(
            new[]
            {
                Event("evt_today", "今天记录", DateTime.UtcNow.AddMinutes(-20)),
                Event("evt_old", "旧记录", DateTime.UtcNow.AddDays(-3))
            },
            new InMemoryMemoryRepository());

        var context = await service.LoadAsync("user_a", new PersonalContextRequest
        {
            MaxEvents = 10,
            Period = "today",
            ClientTimeZone = "UTC"
        });

        Assert.Equal("today", context.Period);
        Assert.Equal("今天", context.WindowLabel);
        Assert.Equal(new[] { "evt_today" }, context.Events.Select(item => item.Id));
    }

    private static PersonalContextService Service(
        IReadOnlyList<LifeEvent> events,
        IMemoryRepository memoryRepository,
        IReadOnlyList<Reminder>? reminders = null)
    {
        return new PersonalContextService(
            new FakeLifeEventService(events),
            memoryRepository,
            new FakeReminderService(reminders ?? Array.Empty<Reminder>()),
            NullLogger<PersonalContextService>.Instance);
    }

    private static LifeEvent Event(string id, string title, DateTime occurredAt, bool isDeleted = false)
    {
        return new LifeEvent
        {
            Id = id,
            UserId = "user_a",
            Title = title,
            Content = title,
            CreatedAt = occurredAt,
            OccurredAt = occurredAt,
            IsDeleted = isDeleted
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
}
