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
    public bool ReadOnly { get; set; } = true;
    public bool WroteData { get; set; } = false;
    public bool Executed { get; set; } = false;
}
