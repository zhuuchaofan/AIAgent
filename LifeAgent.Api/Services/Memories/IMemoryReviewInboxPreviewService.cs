using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryReviewInboxPreviewService
{
    MemoryReviewInboxPreviewData BuildPreview(
        string userId,
        IReadOnlyList<LifeEvent> events,
        IReadOnlyList<Memory>? activeMemories = null);
}
