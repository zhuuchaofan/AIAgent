namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryRetrievalResult
{
    public string MemoryId { get; set; } = string.Empty;
    public string MemoryType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int Importance { get; set; }
    public double Score { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}
