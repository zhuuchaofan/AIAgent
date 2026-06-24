using System.Collections.Generic;

namespace LifeAgent.Api.Models;

/// <summary>
/// GET /api/life/events 响应体
/// </summary>
public class ListEventsResponse
{
    public bool Success { get; set; } = true;
    public string? NextCursor { get; set; }
    public List<TimelineEventDto> Data { get; set; } = new();
}

/// <summary>
/// 时间线事件 DTO，确保不暴露 RawLlmOutput
/// </summary>
public class TimelineEventDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "v1";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OccurredAt { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public string Source { get; set; } = "manual";
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public double ExtractionConfidence { get; set; }
    public bool NeedsReview { get; set; }
}
