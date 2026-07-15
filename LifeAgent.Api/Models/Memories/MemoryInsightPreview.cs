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
    public bool MemoryWriteEnabled { get; set; } = false;
    public IReadOnlyList<MemoryReviewCandidateItem> Candidates { get; set; } = Array.Empty<MemoryReviewCandidateItem>();
}

public sealed class MemoryReviewCandidateItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "theme";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string ReviewStage { get; set; } = "observing";
    public string ReviewStageLabel { get; set; } = "观察中";
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<MemoryReviewSourceItem> Sources { get; set; } = Array.Empty<MemoryReviewSourceItem>();
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string QualityReason { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = "keep_observing";
    public string ReviewStatus { get; set; } = "pending";
    public DateTime? ReviewedAt { get; set; }
    public string? MemoryId { get; set; }
    public bool PreviewOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
}

public sealed class MemoryReviewRememberRequest
{
    public string Content { get; set; } = string.Empty;
    public int? Importance { get; set; }
}

public sealed class MemoryReviewCandidateActionResponse
{
    public bool Success { get; set; } = true;
    public bool PreviewOnly { get; set; } = true;
    public bool MemoryWriteEnabled { get; set; } = false;
    public bool WroteMemory { get; set; } = false;
    public bool WroteReviewState { get; set; } = true;
    public string? MemoryId { get; set; }
    public MemoryReviewCandidateItem Data { get; set; } = new();
}

public sealed class MemoryReviewSourceItem
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

public sealed class MemoryContextPreviewResponse
{
    public bool Success { get; set; } = true;
    public MemoryContextPreviewData Data { get; set; } = new();
}

public sealed class MemoryContextPreviewData
{
    public int ScannedCount { get; set; }
    public bool PreviewOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool MemoryWriteEnabled { get; set; } = false;
    public IReadOnlyList<MemoryContextPreviewItem> Items { get; set; } = Array.Empty<MemoryContextPreviewItem>();
}

public sealed class MemoryContextPreviewItem
{
    public string Kind { get; set; } = "theme";
    public string Text { get; set; } = string.Empty;
    public string ReviewStage { get; set; } = "observing";
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
}

public sealed class MemoryItemsResponse
{
    public bool Success { get; set; } = true;
    public IReadOnlyList<MemoryItemDto> Data { get; set; } = Array.Empty<MemoryItemDto>();
}

public sealed class MemoryItemResponse
{
    public bool Success { get; set; } = true;
    public MemoryItemDto Data { get; set; } = new();
}

public sealed class MemoryItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Importance { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = string.Empty;
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public static MemoryItemDto FromMemory(Memory memory)
    {
        return new MemoryItemDto
        {
            Id = memory.Id,
            Type = memory.Type,
            Content = memory.Content,
            Importance = memory.Importance,
            Confidence = memory.Confidence,
            Source = memory.Source,
            SourceEventIds = memory.SourceEventIds,
            Status = memory.Status,
            CreatedAt = memory.CreatedAt,
            UpdatedAt = memory.UpdatedAt,
            ExpiresAt = memory.ExpiresAt
        };
    }
}
