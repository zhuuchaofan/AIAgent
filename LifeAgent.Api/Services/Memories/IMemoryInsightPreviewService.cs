using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryInsightPreviewService
{
    MemoryInsightPreviewData BuildPreview(string userId, IReadOnlyList<LifeEvent> events);
}
