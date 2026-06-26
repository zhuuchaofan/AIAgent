using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

/// <summary>
/// Agent 执行日志，对应 Firestore 路径：users/{userId}/agent_runs/{runId}
/// 记录每次 Agent 任务（如 daily_summary）的执行情况。
/// </summary>
[FirestoreData]
public class AgentRun
{
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>任务类型，当前支持：daily_summary</summary>
    [FirestoreProperty("taskType")]
    public string TaskType { get; set; } = string.Empty;

    /// <summary>目标日期，格式 YYYY-MM-DD（用户本地时区）</summary>
    [FirestoreProperty("targetDate")]
    public string TargetDate { get; set; } = string.Empty;

    /// <summary>执行状态：success | failed</summary>
    [FirestoreProperty("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>本次传入 LLM 的事件数量</summary>
    [FirestoreProperty("inputEventCount")]
    public int InputEventCount { get; set; }

    /// <summary>
    /// Prompt 摘要（仅保存前 200 字符，非完整 prompt）。
    /// 若 SAVE_FULL_AGENT_PROMPT=true 则保存完整 prompt。
    /// </summary>
    [FirestoreProperty("promptSummary")]
    public string? PromptSummary { get; set; }

    /// <summary>是否保存了完整 prompt（由 SAVE_FULL_AGENT_PROMPT 环境变量控制）</summary>
    [FirestoreProperty("fullPromptSaved")]
    public bool FullPromptSaved { get; set; }

    /// <summary>失败时的错误信息</summary>
    [FirestoreProperty("errorMessage")]
    public string? ErrorMessage { get; set; }

    [FirestoreProperty("startedAt")]
    public DateTime StartedAt { get; set; }

    [FirestoreProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>执行耗时（毫秒）</summary>
    [FirestoreProperty("durationMs")]
    public long DurationMs { get; set; }
}
