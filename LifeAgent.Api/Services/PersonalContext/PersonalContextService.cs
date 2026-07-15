using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using LifeAgent.Api.Services.Plans;

namespace LifeAgent.Api.Services.PersonalContext;

public sealed class PersonalContextService : IPersonalContextService
{
    private const int AbsoluteMaxEvents = 50;
    private const int AbsoluteMaxMemories = 20;
    private const int AbsoluteMaxReminders = 20;
    private const int AbsoluteMaxPlanSignals = 20;

    private readonly ILifeEventService _lifeEventService;
    private readonly IMemoryRepository _memoryRepository;
    private readonly IReminderService _reminderService;
    private readonly IPlanSignalService _planSignalService;
    private readonly ILogger<PersonalContextService> _logger;

    public PersonalContextService(
        ILifeEventService lifeEventService,
        IMemoryRepository memoryRepository,
        IReminderService reminderService,
        IPlanSignalService planSignalService,
        ILogger<PersonalContextService> logger)
    {
        _lifeEventService = lifeEventService;
        _memoryRepository = memoryRepository;
        _reminderService = reminderService;
        _planSignalService = planSignalService;
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
        var maxPlanSignals = NormalizeLimit(request.MaxPlanSignals, AbsoluteMaxPlanSignals);
        var period = NormalizePeriod(request.Period);
        var timeZone = ResolveTimeZone(request.ClientTimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        var events = await LoadEventsAsync(userId, maxEvents, period, timeZone, localNow);
        var memoryResult = await LoadMemoriesAsync(userId, maxMemories);
        var reminderResult = maxReminders <= 0
            ? new ContextReminderResult(Array.Empty<Reminder>(), 0)
            : await LoadPendingRemindersAsync(userId, maxReminders);
        var planSignalResult = maxPlanSignals <= 0
            ? new ContextPlanSignalResult(Array.Empty<PlanSignal>(), 0)
            : await LoadPlanSignalsAsync(userId, maxPlanSignals, cancellationToken);

        return new PersonalContextSnapshot
        {
            Events = events,
            Memories = memoryResult.Items,
            PendingReminders = reminderResult.Items,
            PlanSignals = planSignalResult.Items,
            ActiveMemoryCount = memoryResult.TotalCount,
            PendingReminderCount = reminderResult.TotalCount,
            PlanSignalCount = planSignalResult.TotalCount,
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

    private async Task<ContextMemoryResult> LoadMemoriesAsync(string userId, int maxMemories)
    {
        if (maxMemories <= 0)
        {
            return new ContextMemoryResult(Array.Empty<Memory>(), 0);
        }

        var activeMemories = (await _memoryRepository.ListByUserAsync(
                userId,
                type: null,
                status: MemoryStatus.Active.ToSnakeCaseString()))
            .Where(memory => !IsExpiredMemory(memory))
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
            .ToList();

        return new ContextMemoryResult(
            activeMemories
            .Take(maxMemories)
                .ToList(),
            activeMemories.Count);
    }

    private async Task<ContextReminderResult> LoadPendingRemindersAsync(string userId, int maxReminders)
    {
        try
        {
            var pendingReminders = (await _reminderService.ListRemindersAsync(userId, "pending"))
                .Where(reminder => string.Equals(reminder.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(reminder => reminder.DueAt)
                .ToList();

            return new ContextReminderResult(
                pendingReminders
                .Take(maxReminders)
                    .ToList(),
                pendingReminders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pending reminders for personal context user {UserId}", userId);
            return new ContextReminderResult(Array.Empty<Reminder>(), 0);
        }
    }

    private async Task<ContextPlanSignalResult> LoadPlanSignalsAsync(
        string userId,
        int maxPlanSignals,
        CancellationToken cancellationToken)
    {
        try
        {
            var activeSignals = (await _planSignalService.ListAsync(userId, "active", cancellationToken))
                .Where(signal => string.Equals(signal.Status, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(signal => signal.CreatedAt)
                .ToList();

            return new ContextPlanSignalResult(
                activeSignals
                    .Take(maxPlanSignals)
                    .ToList(),
                activeSignals.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plan signals for personal context user {UserId}", userId);
            return new ContextPlanSignalResult(Array.Empty<PlanSignal>(), 0);
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

    private sealed record ContextMemoryResult(IReadOnlyList<Memory> Items, int TotalCount);

    private sealed record ContextReminderResult(IReadOnlyList<Reminder> Items, int TotalCount);

    private sealed record ContextPlanSignalResult(IReadOnlyList<PlanSignal> Items, int TotalCount);
}
