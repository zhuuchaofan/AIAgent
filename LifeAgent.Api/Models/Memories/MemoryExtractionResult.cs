using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryExtractionResult
{
    public string Status { get; set; } = "skipped";
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public MemoryPreviewActionPayload? Proposal { get; set; }
    public MemoryPollutionDecision? GuardDecision { get; set; }
}
