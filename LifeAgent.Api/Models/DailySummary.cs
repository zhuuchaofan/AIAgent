using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

/// <summary>
/// 每日总结实体，对应 Firestore 路径：users/{userId}/daily_summaries/{date}
/// Document ID 即为日期字符串（格式：YYYY-MM-DD，用户本地时区）。
/// </summary>
[FirestoreData]
public class DailySummary
{
    // ── 系统元数据 ────────────────────────────────────────────────

    /// <summary>Document ID = 用户本地时区日期，如 "2026-06-26"</summary>
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>用户本地时区日期，冗余存储以便查询</summary>
    [FirestoreProperty("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>用户时区（IANA），如 "Asia/Shanghai"</summary>
    [FirestoreProperty("timeZone")]
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>查询范围开始时间（UTC），即用户本地当天 00:00:00 对应的 UTC</summary>
    [FirestoreProperty("periodStartUtc")]
    public DateTime PeriodStartUtc { get; set; }

    /// <summary>查询范围结束时间（UTC），即用户本地当天 23:59:59.999 对应的 UTC</summary>
    [FirestoreProperty("periodEndUtc")]
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>当天查询到的 LifeEvent 数量（isDeleted==false）</summary>
    [FirestoreProperty("eventCount")]
    public int EventCount { get; set; }

    // ── LLM 输出 ────────────────────────────────────────────────

    /// <summary>总结正文。空数据日固定为"这一天还没有记录。"</summary>
    [FirestoreProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>高光事件列表。空数据日为空列表。</summary>
    [FirestoreProperty("highlights")]
    public List<string> Highlights { get; set; } = new();

    /// <summary>情绪标签，如"积极"、"平淡"。空数据日为"暂无记录"。</summary>
    [FirestoreProperty("moodLabel")]
    public string MoodLabel { get; set; } = string.Empty;

    /// <summary>情绪分（1-10），可为 null（空数据日为 null）</summary>
    [FirestoreProperty("moodScore")]
    public double? MoodScore { get; set; }

    /// <summary>建议列表。空数据日为空列表。</summary>
    [FirestoreProperty("suggestions")]
    public List<string> Suggestions { get; set; } = new();

    // ── 生成元数据 ───────────────────────────────────────────────

    /// <summary>生成方式：llm | empty_day</summary>
    [FirestoreProperty("generatedBy")]
    public string GeneratedBy { get; set; } = string.Empty;

    /// <summary>关联的 AgentRun ID</summary>
    [FirestoreProperty("agentRunId")]
    public string AgentRunId { get; set; } = string.Empty;

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>是否由 forceRegenerate=true 触发重新生成</summary>
    [FirestoreProperty("forceRegenerated")]
    public bool ForceRegenerated { get; set; }
}
