using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

/// <summary>
/// 生活事件存取服务接口（依赖倒置，方便后续单元测试 Mock）
/// </summary>
public interface ILifeEventService
{
    /// <summary>
    /// 将一条 LifeEvent 持久化写入 Firestore。
    /// 写入路径：users/{userId}/life_events/{eventId}
    /// </summary>
    /// <param name="userId">当前认证用户 ID，必须来自后端验签，不接受外部传入</param>
    /// <param name="lifeEvent">待写入的事件对象（UserId/Id/CreatedAt 由本方法内部强制覆盖）</param>
    /// <returns>写入成功后，带有最终 Id 的 LifeEvent 对象</returns>
    Task<LifeEvent> SaveEventAsync(string userId, LifeEvent lifeEvent);

    /// <summary>
    /// 按 occurredAt DESC 分页查询指定用户的生活事件列表。
    /// 查询路径：users/{userId}/life_events
    /// </summary>
    /// <param name="userId">当前认证用户 ID</param>
    /// <param name="type">事件类型过滤（"all" 或 null = 不过滤；其余值精确匹配 type 字段）</param>
    /// <param name="limit">每页条数，默认 20，上限 100</param>
    /// <param name="cursor">分页游标，Base64("occurredAt_ISO|documentId")，为 null 查第一页</param>
    Task<ListEventsResult> ListEventsAsync(
        string userId,
        string? type  = null,
        int    limit  = 20,
        string? cursor = null);

    /// <summary>
    /// 根据 ID 获取事件（按用户隔离）
    /// </summary>
    Task<LifeEvent?> GetEventAsync(string userId, string eventId);
}
