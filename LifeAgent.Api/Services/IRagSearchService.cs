using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IRagSearchService
{
    Task<List<VectorSearchResult>> SearchAsync(
        string userId,
        string query,
        IReadOnlyList<string>? documentIds,
        int? topK,
        CancellationToken cancellationToken = default);
}
