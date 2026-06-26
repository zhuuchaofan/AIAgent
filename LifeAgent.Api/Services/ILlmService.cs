using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

/// <summary>
/// LLM 解析服务接口。
/// Phase 1 使用 MockLlmService 实现；后续可替换为真实 GeminiLlmService。
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// 将用户自然语言文本解析为结构化事件。
    /// </summary>
    /// <param name="text">用户原始输入</param>
    /// <param name="timeZone">用户时区（IANA），用于未来自然语言时间解析</param>
    Task<ParsedEvent> ParseAsync(string text, string timeZone);
}

/// <summary>LLM 解析结果（中间层，不直接写 Firestore）</summary>
public class ParsedEvent
{
    public string Type { get; set; } = "unknown";
    public string Title { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; } = 2;
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public double ExtractionConfidence { get; set; } = 0.5;
    public bool NeedsReview { get; set; } = true;
    public bool DetectedReminderIntent { get; set; } = false;

    [System.Text.Json.Serialization.JsonPropertyName("reminderTitle")]
    public string? ReminderTitle { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reminderDueAt")]
    public string? ReminderDueAtIso { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reminderDescription")]
    public string? ReminderDescription { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reminder")]
    public ReminderNode? Reminder { get; set; }

    public string? RawLlmOutput { get; set; }
}

/// <summary>
/// 大模型输出的嵌套提醒节点结构，对应契约中的 reminder 对象
/// </summary>
public class ReminderNode
{
    [System.Text.Json.Serialization.JsonPropertyName("hasIntent")]
    public bool HasIntent { get; set; } = false;

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string? Title { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("dueAtIso8601")]
    public string? DueAtIso8601 { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("parseStatus")]
    public string ParseStatus { get; set; } = "none";
}

