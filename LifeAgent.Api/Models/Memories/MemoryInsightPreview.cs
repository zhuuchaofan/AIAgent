namespace LifeAgent.Api.Models.Memories;

public sealed class MemoryInsightPreviewResponse
{
    public bool Success { get; set; } = true;
    public MemoryInsightPreviewData Data { get; set; } = new();
}

public sealed class MemoryInsightPreviewData
{
    public int ScannedCount { get; set; }
    public bool PreviewOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool MemoryWriteEnabled { get; set; } = false;
    public IReadOnlyList<MemoryInsightPreviewItem> Insights { get; set; } = Array.Empty<MemoryInsightPreviewItem>();
}

public sealed class MemoryInsightPreviewItem
{
    public string Kind { get; set; } = "theme";
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
}
