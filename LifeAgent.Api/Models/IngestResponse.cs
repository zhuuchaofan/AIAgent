namespace LifeAgent.Api.Models;

/// <summary>POST /api/life/ingest 成功响应体（与 api_spec.md 对齐）</summary>
public class IngestResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>是否检测到提醒意图（含"提醒""明天提醒"等关键词）</summary>
    public bool DetectedReminderIntent { get; set; }

    /// <summary>Phase 1 固定为 false，提醒功能后续阶段开放</summary>
    public bool ReminderCreated { get; set; } = false;

    public IngestResponseData? Data { get; set; }
}

/// <summary>响应体中的 data 节点，对应已写入 Firestore 的 LifeEvent 核心字段</summary>
public class IngestResponseData
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "v1";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string OccurredAt { get; set; } = string.Empty;   // ISO 8601 UTC
    public string CreatedAt { get; set; } = string.Empty;    // ISO 8601 UTC
    public string TimeZone { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public string Source { get; set; } = "manual";
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public double ExtractionConfidence { get; set; }
    public bool NeedsReview { get; set; }
}

/// <summary>统一错误响应体</summary>
public class ErrorResponse
{
    public bool Success { get; set; } = false;
    public ErrorDetail Error { get; set; } = new();
}

public class ErrorDetail
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Details { get; set; }
}
