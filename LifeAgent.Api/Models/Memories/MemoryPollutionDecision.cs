namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryPollutionDecision
{
    public string Action { get; set; } = "allow";
    public bool Blocked { get; set; }
    public bool ReviewRequired { get; set; }
    public string Reason { get; set; } = string.Empty;
    public MemoryMergeCandidate? MergeCandidate { get; set; }
    public MemoryConflictResult? ConflictResult { get; set; }
}
