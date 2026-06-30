namespace LifeAgent.Api.Models.Memories;

/// <summary>
/// 长期记忆的生命周期状态
/// </summary>
public enum MemoryStatus
{
    /// <summary>待用户确认（前端展示卡片）</summary>
    PendingConfirm,

    /// <summary>正式生效并参与 Planner 召回</summary>
    Active,

    /// <summary>达成/过期/废弃后的归档（不再参与日常召回，但可供深度回顾）</summary>
    Archived
}

/// <summary>
/// 长期记忆生命周期的字符串转换与辅助映射工具
/// </summary>
public static class MemoryStatusHelper
{
    private static readonly Dictionary<string, MemoryStatus> StringToEnumMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pending_confirm", MemoryStatus.PendingConfirm },
        { "active", MemoryStatus.Active },
        { "archived", MemoryStatus.Archived }
    };

    private static readonly Dictionary<MemoryStatus, string> EnumToStringMap = new()
    {
        { MemoryStatus.PendingConfirm, "pending_confirm" },
        { MemoryStatus.Active, "active" },
        { MemoryStatus.Archived, "archived" }
    };

    /// <summary>
    /// 验证小写下划线字符串是否是合法的 MemoryStatus
    /// </summary>
    public static bool IsValid(string statusString)
    {
        return !string.IsNullOrWhiteSpace(statusString) && StringToEnumMap.ContainsKey(statusString);
    }

    /// <summary>
    /// 将字符串转换为 MemoryStatus 枚举，若格式非法则返回 null
    /// </summary>
    public static MemoryStatus? FromString(string statusString)
    {
        if (string.IsNullOrWhiteSpace(statusString)) return null;
        return StringToEnumMap.TryGetValue(statusString, out var val) ? val : null;
    }

    /// <summary>
    /// 将 MemoryStatus 枚举转换为标准的数据库小写下划线字符串形式
    /// </summary>
    public static string ToSnakeCaseString(this MemoryStatus status)
    {
        return EnumToStringMap.TryGetValue(status, out var val) ? val : status.ToString().ToLowerInvariant();
    }
}
