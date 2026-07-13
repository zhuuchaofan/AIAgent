using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models.Memories;

/// <summary>
/// 长期记忆实体，对应 Firestore 路径：users/{userId}/memories/{memoryId}
/// 字段设计严格遵循 docs/phase6_0_memory_engine_architecture.md 中的定义。
/// </summary>
[FirestoreData]
public class Memory
{
    // ── 系统元数据（由后端生成，不接受前端或 LLM 直接覆盖）────────────────

    /// <summary>记忆唯一标识，由后端生成，格式：mem_{32位Guid:N}</summary>
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属用户 UID，由认证上下文注入</summary>
    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>大模型对当前记忆提报时的置信度评分 (0.0 - 1.0)</summary>
    [FirestoreProperty("confidence")]
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// 被召回/引用总次数统计
    /// </summary>
    [FirestoreProperty("recCount")]
    public int RecCount { get; set; } = 0;

    /// <summary>
    /// 上一次被 Agent 检索或引用的时间
    /// </summary>
    [FirestoreProperty("lastRecalledAt")]
    public DateTime? LastRecalledAt { get; set; }

    /// <summary>长期记忆创建的 UTC 时间</summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>长期记忆最近更新的 UTC 时间</summary>
    [FirestoreProperty("updatedAt")]
    public DateTime? UpdatedAt { get; set; }


    // ── 业务及控制流字段（大模型与用户交互承载）───────────────────────────

    /// <summary>长期记忆分类，对应 MemoryType 转换为下划线蛇形命名（如 preference, constraint）</summary>
    [FirestoreProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>长期记忆状态，对应 MemoryStatus 蛇形命名（如 pending_confirm, active, archived）</summary>
    [FirestoreProperty("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>记忆的内容原文（如“对花生严重过敏，禁止推荐任何花生食品”）</summary>
    [FirestoreProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>重要程度等级 (1-5)</summary>
    [FirestoreProperty("importance")]
    public int Importance { get; set; } = 3;

    /// <summary>
    /// 记忆来源标记：manual_entry = 用户手动录入，agent_confirmed = 用户确认后的 Agent 提取
    /// </summary>
    [FirestoreProperty("source")]
    public string Source { get; set; } = "manual_entry";

    /// <summary>
    /// 支撑这条记忆的生活记录 id 列表。
    /// </summary>
    [FirestoreProperty("sourceEventIds")]
    public List<string> SourceEventIds { get; set; } = new();

    /// <summary>
    /// 若由 Agent 提报，记录触发此记忆的 Pending Agent Action ID
    /// </summary>
    [FirestoreProperty("agentActionId")]
    public string? AgentActionId { get; set; }

    /// <summary>
    /// 短期上下文临时记忆的物理过期时间。对于非 temporary_context 类型的记忆此值为 null
    /// </summary>
    [FirestoreProperty("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 动态多租户属性，提供结构化信息沉淀，禁止保存敏感词和完整的原始 Payload 块
    /// </summary>
    [FirestoreProperty("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
