using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

/// <summary>
/// 提醒事项管理服务接口。
/// </summary>
public interface IReminderService
{
    /// <summary>
    /// 获取指定用户的提醒事项列表，按 dueAt 升序排列。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="status">过滤状态（pending, completed, cancelled）</param>
    Task<List<Reminder>> ListRemindersAsync(string userId, string status);

    /// <summary>
    /// 获取单个提醒事项。
    /// </summary>
    Task<Reminder?> GetReminderAsync(string userId, string reminderId);

    /// <summary>
    /// 更新提醒事项的状态和/或截止时间，包含严格的状态机与属性互斥规则。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="reminderId">提醒事项 ID</param>
    /// <param name="status">新状态（可选）</param>
    /// <param name="dueAt">新截止时间（可选）</param>
    /// <returns>更新成功返回 true，文档不存在或越权返回 false</returns>
    Task<bool> UpdateReminderAsync(string userId, string reminderId, string? status, DateTime? dueAt);
}
