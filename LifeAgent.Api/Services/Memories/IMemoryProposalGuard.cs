using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryProposalGuard
{
    MemoryPollutionDecision Evaluate(
        MemoryPreviewActionPayload proposal,
        IReadOnlyList<Memory> existingMemories);
}
