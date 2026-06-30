namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryRetrievalRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? Query { get; set; }
    public IReadOnlyList<string>? Types { get; set; }
    public IReadOnlyList<string>? Statuses { get; set; }
    public int Limit { get; set; } = 5;
    public bool IncludeArchived { get; set; }
}
