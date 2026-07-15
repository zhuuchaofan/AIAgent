using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Api.Services.PersonalContext;

public sealed class PersonalContextService : IPersonalContextService
{
    private const int AbsoluteMaxEvents = 50;
    private const int AbsoluteMaxMemories = 20;
    private const int AbsoluteMaxReminders = 20;

    private readonly ILifeEventService _lifeEventService;
    private readonly IMemoryRepository _memoryRepository;
    private readonly IReminderService _reminderService;
    private readonly ILogger<PersonalContextService> _logger;

    public PersonalContextService(
        ILifeEventService lifeEventService,
        IMemoryRepository memoryRepository,
        IReminderService reminderService,
        ILogger<PersonalContextService> logger)
    {
        _lifeEventService = lifeEventService;
        _memoryRepository = memoryRepository;
        _reminderService = reminderService;
        _logger = logger;
    }

    public async Task<PersonalContextSnapshot> LoadAsync(
        string userId,
        PersonalContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        request ??= new PersonalContextRequest();
        var maxEvents = NormalizeLimit(request.MaxEvents, AbsoluteMaxEvents);
        var maxMemories = NormalizeLimit(request.MaxMemories, AbsoluteMaxMemories);
        var maxReminders = NormalizeLimit(request.MaxReminders, AbsoluteMaxReminders);
        var period = NormalizePeriod(request.Period);
        var timeZone = ResolveTimeZone(request.ClientTimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        var events = await LoadEventsAsync(userId, maxEvents, period, timeZone, localNow);
        var memories = await LoadMemoriesAsync(userId, maxMemories);
        var reminders = maxReminders <= 0
            ? Array.Empty<Reminder>()
            : await LoadPendingRemindersAsync(userId, maxReminders);

        return new PersonalContextSnapshot
        {
            Events = events,
            Memories = memories,
            PendingReminders = reminders,
            Period = period,
            WindowLabel = ToWindowLabel(period)
        };
    }

    private async Task<IReadOnlyList<LifeEvent>> LoadEventsAsync(
        string userId,
        int maxEvents,
        string period,
        TimeZoneInfo timeZone,
        DateTime localNow)
    {
        if (maxEvents <= 0)
        {
            return Array.Empty<LifeEvent>();
        }

        var result = await _lifeEventService.ListEventsAsync(
            userId,
            type: "all",
            limit: maxEvents,
            cursor: null,
            tag: null);

        return result.Data
            .Where(item => !item.IsDeleted)
            .Where(item => IsInPeriod(item, period, timeZone, localNow))
            .OrderByDescending(GetOccurredAt)
            .Take(maxEvents)
            .ToList();
    }

    private async Task<IReadOnlyList<Memory>> LoadMemoriesAsync(string userId, int maxMemories)
    {
        if (maxMemories <= 0)
        {
            return Array.Empty<Memory>();
        }

        return (await _memoryRepository.ListByUserAsync(
                userId,
                type: null,
                status: MemoryStatus.Active.ToSnakeCaseString()))
            .Where(memory => !IsExpiredMemory(memory))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
            .Take(maxMemories)
            .ToList();
    }

    private async Task<IReadOnlyList<Reminder>> LoadPendingRemindersAsync(string userId, int maxReminders)
    {
        try
        {
            return (await _reminderService.ListRemindersAsync(userId, "pending"))
                .Where(reminder => string.Equals(reminder.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(reminder => reminder.DueAt)
                .Take(maxReminders)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pending reminders for personal context user {UserId}", userId);
            return Array.Empty<Reminder>();
        }
    }

    private static int NormalizeLimit(int value, int absoluteMax)
    {
        if (value <= 0)
        {
            return 0;
        }

        return Math.Min(value, absoluteMax);
    }

    private static string NormalizePeriod(string? period)
    {
        return period?.Trim().ToLowerInvariant() switch
        {
            "today" => "today",
            "week" => "week",
            _ => "recent"
        };
    }

    private static string ToWindowLabel(string period)
    {
        return period switch
        {
            "today" => "今天",
            "week" => "本周",
            _ => "最近"
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZone) ? "Asia/Shanghai" : timeZone.Trim());
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool IsInPeriod(LifeEvent item, string period, TimeZoneInfo timeZone, DateTime localNow)
    {
        if (period == "recent")
        {
            return true;
        }

        var occurredAt = GetOccurredAt(item);
        var utc = occurredAt.Kind == DateTimeKind.Utc
            ? occurredAt
            : DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc);
        var localOccurredAt = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);

        if (period == "today")
        {
            return localOccurredAt.Date == localNow.Date;
        }

        var daysFromMonday = ((int)localNow.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = localNow.Date.AddDays(-daysFromMonday);
        return localOccurredAt >= weekStart;
    }

    private static DateTime GetOccurredAt(LifeEvent item)
    {
        return item.OccurredAt == default ? item.CreatedAt : item.OccurredAt;
    }

    private static bool IsExpiredMemory(Memory memory)
    {
        return memory.ExpiresAt.HasValue && memory.ExpiresAt.Value <= DateTime.UtcNow;
    }
}
