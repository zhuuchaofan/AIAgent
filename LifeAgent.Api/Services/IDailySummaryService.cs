using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

/// <summary>
/// 每日总结服务接口。
/// </summary>
public interface IDailySummaryService
{
    /// <summary>
    /// 生成（或返回缓存的）指定日期的每日总结。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="targetDate">目标日期，格式 YYYY-MM-DD（用户本地时区）</param>
    /// <param name="timeZone">用户时区（IANA），如 "Asia/Shanghai"</param>
    /// <param name="forceRegenerate">若为 true，强制重新生成，忽略缓存</param>
    /// <returns>(DailySummary 实体, 是否命中缓存)</returns>
    Task<(DailySummary Summary, bool Cached)> GenerateSummaryAsync(
        string userId, string targetDate, string timeZone, bool forceRegenerate);

    /// <summary>
    /// 按日期查询已生成的每日总结（无则返回 null）。
    /// </summary>
    Task<DailySummary?> GetSummaryByDateAsync(string userId, string date);
}
