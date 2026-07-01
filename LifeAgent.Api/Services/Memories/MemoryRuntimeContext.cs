namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryRuntimeContext
{
    public bool Enabled { get; init; }
    public string Status { get; init; } = "disabled";
    public string? SkippedReason { get; init; }
    public int ResultCount { get; init; }
    public int MaxResults { get; init; }
    public string? FormattedContext { get; init; }
    public IReadOnlyList<MemoryRuntimeContextItem> Results { get; init; } = Array.Empty<MemoryRuntimeContextItem>();

    public static MemoryRuntimeContext Disabled(string reason = "feature_disabled")
    {
        return new MemoryRuntimeContext
        {
            Enabled = false,
            Status = "disabled",
            SkippedReason = reason
        };
    }

    public static MemoryRuntimeContext Skipped(string reason, bool enabled = true)
    {
        return new MemoryRuntimeContext
        {
            Enabled = enabled,
            Status = "skipped",
            SkippedReason = reason
        };
    }

    public object ToDiagnostics()
    {
        return new
        {
            enabled = Enabled,
            status = Status,
            skippedReason = SkippedReason,
            resultCount = ResultCount,
            maxResults = MaxResults,
            contextAvailable = !string.IsNullOrWhiteSpace(FormattedContext)
        };
    }
}

public sealed class MemoryRuntimeContextItem
{
    public string MemoryId { get; init; } = string.Empty;
    public string MemoryType { get; init; } = string.Empty;
    public double Score { get; init; }
    public string Reason { get; init; } = string.Empty;
}
