namespace LifeAgent.Api.Models;

public class LifeReviewRequest
{
    public string? ClientTimeZone { get; set; }
    public int? Limit { get; set; }
    public string? Period { get; set; }
}

public class LifeReviewResponse
{
    public bool Success { get; set; } = true;
    public string Period { get; set; } = "recent";
    public string WindowLabel { get; set; } = "最近";
    public IReadOnlyList<LifeReviewCard> Cards { get; set; } = Array.Empty<LifeReviewCard>();
    public IReadOnlyList<LifeReviewTheme> ReviewThemes { get; set; } = Array.Empty<LifeReviewTheme>();
    public IReadOnlyList<LifeReviewContinuityHint> ContinuityHints { get; set; } = Array.Empty<LifeReviewContinuityHint>();
    public IReadOnlyList<LifeReviewSourceEvent> SourceEvents { get; set; } = Array.Empty<LifeReviewSourceEvent>();
    public int UsedEventCount { get; set; }
    public int UsedMemoryCount { get; set; }
    public int UsedPlanSignalCount { get; set; }
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
    public IReadOnlyList<LifeReviewEvidenceHint> EvidenceHints { get; set; } = Array.Empty<LifeReviewEvidenceHint>();
}

public class LifeReviewTheme
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public IReadOnlyList<LifeReviewEvidenceHint> EvidenceHints { get; set; } = Array.Empty<LifeReviewEvidenceHint>();
}

public class LifeReviewContinuityHint
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class LifeReviewEvidenceHint
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
}

public class LifeReviewSourceEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OccurredAt { get; set; } = string.Empty;
}

public class LifeReviewKeepRequest
{
    public string CardId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();
}
