namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryMergeCandidate
{
    public bool HasCandidate { get; set; }
    public string? ExistingMemoryId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}
