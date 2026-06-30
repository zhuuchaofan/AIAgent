using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryExtractionService
{
    IReadOnlyList<MemoryExtractionResult> Extract(MemoryExtractionRequest request);
}
