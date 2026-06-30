namespace LifeAgent.Api.Models.Memories;

/// <summary>
/// 长期记忆的 12 种核心分类
/// </summary>
public enum MemoryType
{
    /// <summary>个人生活中的重大事实记录</summary>
    LifeEvent,

    /// <summary>主观的偏好/习惯选择</summary>
    Preference,

    /// <summary>短期、中期、长期目标</summary>
    Goal,

    /// <summary>自动归纳或手动确认的周期性行为习惯</summary>
    Habit,

    /// <summary>社交与人际网络属性</summary>
    Relationship,

    /// <summary>高价值的长效私有知识</summary>
    Knowledge,

    /// <summary>复合任务体系/项目</summary>
    Project,

    /// <summary>关系人画像</summary>
    Person,

    /// <summary>空间地理常用位置</summary>
    Location,

    /// <summary>日常通勤等固定惯例</summary>
    Routine,

    /// <summary>核心红线安全约束与健康红线</summary>
    Constraint,

    /// <summary>临时的短期关注快照（如出差、特定任务期）</summary>
    TemporaryContext
}

/// <summary>
/// 长期记忆分类的字符串转换与辅助映射工具
/// </summary>
public static class MemoryTypeHelper
{
    private static readonly Dictionary<string, MemoryType> StringToEnumMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "life_event", MemoryType.LifeEvent },
        { "preference", MemoryType.Preference },
        { "goal", MemoryType.Goal },
        { "habit", MemoryType.Habit },
        { "relationship", MemoryType.Relationship },
        { "knowledge", MemoryType.Knowledge },
        { "project", MemoryType.Project },
        { "person", MemoryType.Person },
        { "location", MemoryType.Location },
        { "routine", MemoryType.Routine },
        { "constraint", MemoryType.Constraint },
        { "temporary_context", MemoryType.TemporaryContext }
    };

    private static readonly Dictionary<MemoryType, string> EnumToStringMap = new()
    {
        { MemoryType.LifeEvent, "life_event" },
        { MemoryType.Preference, "preference" },
        { MemoryType.Goal, "goal" },
        { MemoryType.Habit, "habit" },
        { MemoryType.Relationship, "relationship" },
        { MemoryType.Knowledge, "knowledge" },
        { MemoryType.Project, "project" },
        { MemoryType.Person, "person" },
        { MemoryType.Location, "location" },
        { MemoryType.Routine, "routine" },
        { MemoryType.Constraint, "constraint" },
        { MemoryType.TemporaryContext, "temporary_context" }
    };

    /// <summary>
    /// 验证小写下划线字符串是否是合法的 MemoryType
    /// </summary>
    public static bool IsValid(string typeString)
    {
        return !string.IsNullOrWhiteSpace(typeString) && StringToEnumMap.ContainsKey(typeString);
    }

    /// <summary>
    /// 将字符串转换为 MemoryType 枚举，若格式非法则返回 null
    /// </summary>
    public static MemoryType? FromString(string typeString)
    {
        if (string.IsNullOrWhiteSpace(typeString)) return null;
        return StringToEnumMap.TryGetValue(typeString, out var val) ? val : null;
    }

    /// <summary>
    /// 将 MemoryType 枚举转换为标准的数据库小写下划线字符串形式
    /// </summary>
    public static string ToSnakeCaseString(this MemoryType type)
    {
        return EnumToStringMap.TryGetValue(type, out var val) ? val : type.ToString().ToLowerInvariant();
    }
}
