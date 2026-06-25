using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

/// <summary>
/// 生活事件实体，对应 Firestore 路径：users/{userId}/life_events/{eventId}
/// 字段设计严格遵循 docs/firestore_schema.md 中的 Life Events 定义。
/// </summary>
[FirestoreData]
public class LifeEvent
{
    // ── 系统元数据（全部由后端生成，严禁 LLM 或前端写入）───────────────

    /// <summary>事件唯一标识，由后端生成（Firestore Document ID）</summary>
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属用户 UID，从 FirebaseAuthMiddleware 注入，不接受外部传入</summary>
    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>数据入库时间，UTC，由后端服务器写入时生成</summary>
    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>记录最后一次被编辑的时间</summary>
    [FirestoreProperty("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>软删除标识，查询 Timeline 时默认过滤</summary>
    [FirestoreProperty("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    /// <summary>记录被软删除的 UTC 时间</summary>
    [FirestoreProperty("deletedAt")]
    public DateTime? DeletedAt { get; set; }


    // ── 大模型/Mock 可写字段（仅限纯业务内容）────────────────────────

    /// <summary>事件核心分类：cycling | home | cat | life | unknown</summary>
    [FirestoreProperty("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>结构化数据模式版本，默认 "v1"，用于字段向后兼容</summary>
    [FirestoreProperty("schemaVersion")]
    public string SchemaVersion { get; set; } = "v1";

    /// <summary>大模型生成的简短标题</summary>
    [FirestoreProperty("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>用户原始输入内容（原文，不做修改）</summary>
    [FirestoreProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 事件实际发生时间，统一存储为 UTC。
    /// Phase 1 默认等于 CreatedAt，不解析"昨天/上周"等自然语言时间。
    /// </summary>
    [FirestoreProperty("occurredAt")]
    public DateTime OccurredAt { get; set; }

    /// <summary>用户录入时的本地时区（IANA 格式），例如 "Asia/Tokyo"</summary>
    [FirestoreProperty("timeZone")]
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>检索标签列表，大模型提取</summary>
    [FirestoreProperty("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// 事件重要程度 1-5（大模型判断）。
    /// 3 及以上为月总结时捞取的高光事件。
    /// </summary>
    [FirestoreProperty("importance")]
    public int Importance { get; set; }

    /// <summary>
    /// 记录来源，由后端决定，大模型不可修改。
    /// manual = 用户手动录入，agent = Agent 触发
    /// </summary>
    [FirestoreProperty("source")]
    public string Source { get; set; } = "manual";

    /// <summary>
    /// 与 type 强绑定的动态结构化数据（Map）。
    /// 未提取到的可选字段必须直接省略 Key，严禁写入 0 / 0.0 等默认值。
    /// </summary>
    [FirestoreProperty("structuredData")]
    public Dictionary<string, object> StructuredData { get; set; } = new();

    /// <summary>LLM 提取置信度（0.0 - 1.0），低于 0.7 时 NeedsReview 强制为 true</summary>
    [FirestoreProperty("extractionConfidence")]
    public double ExtractionConfidence { get; set; }

    /// <summary>是否需要人工确认（置信度过低或解析失败时置为 true）</summary>
    [FirestoreProperty("needsReview")]
    public bool NeedsReview { get; set; }

    /// <summary>
    /// 调试用：原始大模型输出 JSON 文本（可空）。
    /// 生产环境 API 响应中不返回该字段；Timeline 列表永远不返回。
    /// </summary>
    [FirestoreProperty("rawLlmOutput")]
    public string? RawLlmOutput { get; set; }
}
