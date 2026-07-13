using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services;

public interface ILifeReviewMemoryCandidateService
{
    Task<MemoryReviewCandidateActionResponse> KeepFromReviewCardAsync(string userId, LifeReviewKeepRequest request);
}
