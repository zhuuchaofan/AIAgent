using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.Plans;

public interface IPlanSignalService
{
    Task<PlanSignal> CreateAsync(string userId, PlanSignal signal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanSignal>> ListAsync(string userId, string status = "active", CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(string userId, string signalId, CancellationToken cancellationToken = default);
}
