using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryRetrievalService
{
    Task<IReadOnlyList<MemoryRetrievalResult>> RetrieveAsync(
        MemoryRetrievalRequest request,
        CancellationToken cancellationToken = default);
}
