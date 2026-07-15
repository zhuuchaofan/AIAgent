namespace LifeAgent.Api.Models;

public class LifeChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ClientTimeZone { get; set; }
}

public class LifeChatResponse
{
    public bool Success { get; set; } = true;
    public string Response { get; set; } = string.Empty;
    public int UsedEventCount { get; set; }
    public int UsedMemoryCount { get; set; }
    public int UsedReminderCount { get; set; }
    public int UsedPlanSignalCount { get; set; }
    public LifeChatUsedContext UsedContext { get; set; } = new();
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public class LifeChatUsedContext
{
    public IReadOnlyList<LifeChatUsedContextItem> Items { get; set; } = Array.Empty<LifeChatUsedContextItem>();
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}

public class LifeChatUsedContextItem
{
    public string Id { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
