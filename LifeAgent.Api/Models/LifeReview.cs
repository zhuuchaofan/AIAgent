namespace LifeAgent.Api.Models;

public class LifeReviewRequest
{
    public string? ClientTimeZone { get; set; }
    public int? Limit { get; set; }
}

public class LifeReviewResponse
{
    public bool Success { get; set; } = true;
    public IReadOnlyList<LifeReviewCard> Cards { get; set; } = Array.Empty<LifeReviewCard>();
    public IReadOnlyList<LifeReviewSourceEvent> SourceEvents { get; set; } = Array.Empty<LifeReviewSourceEvent>();
    public int UsedEventCount { get; set; }
    public int UsedMemoryCount { get; set; }
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public class LifeReviewCard
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
}

public class LifeReviewSourceEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OccurredAt { get; set; } = string.Empty;
}
