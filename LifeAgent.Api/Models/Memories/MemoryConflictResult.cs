namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryConflictResult
{
    public bool HasConflict { get; set; }
    public string? ExistingMemoryId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string ConflictKind { get; set; } = "none";
    public string Reason { get; set; } = string.Empty;
}
