using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface ILifeReviewService
{
    Task<LifeReviewResponse> BuildReviewAsync(string userId, LifeReviewRequest request);
}
