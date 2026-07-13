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

public sealed class MemoryReviewInboxPreviewResponse
{
    public bool Success { get; set; } = true;
    public MemoryReviewInboxPreviewData Data { get; set; } = new();
}

public sealed class MemoryReviewInboxPreviewData
{
    public int ScannedCount { get; set; }
    public bool PreviewOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public IReadOnlyList<MemoryReviewCandidateItem> Candidates { get; set; } = Array.Empty<MemoryReviewCandidateItem>();
}

public sealed class MemoryReviewCandidateItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "theme";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<MemoryReviewSourceItem> Sources { get; set; } = Array.Empty<MemoryReviewSourceItem>();
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool PreviewOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
}

public sealed class MemoryReviewSourceItem
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
