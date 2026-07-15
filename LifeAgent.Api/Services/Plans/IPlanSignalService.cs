using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.Plans;

public interface IPlanSignalService
{
    Task<PlanSignal> CreateAsync(string userId, PlanSignal signal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanSignal>> ListAsync(string userId, string status = "active", CancellationToken cancellationToken = default);
    Task<PlanSignal?> GetAsync(string userId, string signalId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(string userId, string signalId, CancellationToken cancellationToken = default);
    Task<PlanSignalReminderConversionResult?> ConvertReminderSignalAsync(
        string userId,
        string signalId,
        PlanSignalReminderConversionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PlanSignalReminderConversionRequest(
    DateTime DueAt,
    string? Timezone,
    string? Title,
    string? Description);

public sealed record PlanSignalReminderConversionResult(
    PlanSignal Signal,
    Reminder Reminder);
