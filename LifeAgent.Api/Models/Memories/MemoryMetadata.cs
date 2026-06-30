namespace LifeAgent.Api.Models.Memories;

/// <summary>
/// 长期记忆元数据中常用的标准 Key 常量定义，确保不同记忆类型在 Metadata (Firestore 嵌套 Map) 里的键名一致性。
/// </summary>
public static class MemoryMetadataKeys
{
    /// <summary>通用：置信度评分 (0-1)，代表大模型判断的置信度</summary>
    public const string Confidence = "confidence";

    /// <summary>通用：二级分类细分标签（例如 Preference 下的 "food"、"coding"）</summary>
    public const string Category = "category";

    /// <summary>Relationship / Person：重要关系人的生日 (MM-DD 或 YYYY-MM-DD)</summary>
    public const string Birthday = "birthday";

    /// <summary>Relationship：纪念日日期描述</summary>
    public const string Anniversary = "anniversary";

    /// <summary>Project：项目阶段性状态（如 "planning", "in_progress", "completed"）</summary>
    public const string ProjectStatus = "projectStatus";

    /// <summary>Constraint：安全约束的严重等级（如 "critical" - 强硬过敏红线, "warning" - 软性偏好约束）</summary>
    public const string Severity = "severity";
}
