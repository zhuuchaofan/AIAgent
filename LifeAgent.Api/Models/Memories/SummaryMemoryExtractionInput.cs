namespace LifeAgent.Api.Models.Memories;

public sealed class SummaryMemoryExtractionInput
{
    public string SummaryId { get; set; } = string.Empty;
    public DateTime? SummaryDate { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}
