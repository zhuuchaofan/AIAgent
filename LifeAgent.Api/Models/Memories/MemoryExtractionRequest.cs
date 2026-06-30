namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryExtractionRequest
{
    public string UserId { get; set; } = string.Empty;
    public IReadOnlyList<TimelineMemoryExtractionInput> TimelineItems { get; set; } = Array.Empty<TimelineMemoryExtractionInput>();
    public IReadOnlyList<SummaryMemoryExtractionInput> Summaries { get; set; } = Array.Empty<SummaryMemoryExtractionInput>();
}
