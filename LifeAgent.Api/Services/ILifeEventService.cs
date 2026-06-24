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
}
