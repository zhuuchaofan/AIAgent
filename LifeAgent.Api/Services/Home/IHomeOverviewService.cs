using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.Home;

public interface IHomeOverviewService
{
    Task<HomeOverviewData> BuildAsync(
        string userId,
        int limit = 20,
        string? timeZone = null,
        CancellationToken cancellationToken = default);
}
