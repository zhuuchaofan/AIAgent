using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.PersonalContext;

public interface IPersonalContextService
{
    Task<PersonalContextSnapshot> LoadAsync(
        string userId,
        PersonalContextRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalContextRequest
{
    public int MaxEvents { get; init; } = 30;
    public int MaxMemories { get; init; } = 12;
    public int MaxReminders { get; init; } = 0;
    public int MaxPlanSignals { get; init; } = 0;
    public string Period { get; init; } = "recent";
    public string? ClientTimeZone { get; init; }
}

public sealed class PersonalContextSnapshot
{
    public IReadOnlyList<LifeEvent> Events { get; init; } = Array.Empty<LifeEvent>();
    public IReadOnlyList<Memory> Memories { get; init; } = Array.Empty<Memory>();
    public IReadOnlyList<Reminder> PendingReminders { get; init; } = Array.Empty<Reminder>();
    public IReadOnlyList<PlanSignal> PlanSignals { get; init; } = Array.Empty<PlanSignal>();
    public int ActiveMemoryCount { get; init; }
    public int PendingReminderCount { get; init; }
    public int PlanSignalCount { get; init; }
    public string Period { get; init; } = "recent";
    public string WindowLabel { get; init; } = "最近";
}
