using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

/// <summary>
/// 长期记忆仓储接口定义。提供对长期记忆实体的增删改查及生命周期归档操作。
/// 接口实现必须严格遵守按用户 (userId) 进行数据物理隔离与权限边界控制。
/// </summary>
public interface IMemoryRepository
{
    /// <summary>
    /// 在数据库中创建一条长期记忆实体。
    /// 强制覆盖传入实体的 Id (格式为 mem_{Guid:N})、UserId，CreatedAt 并生成当前 UTC 时间。
    /// </summary>
    Task<Memory> CreateAsync(string userId, Memory memory);

    /// <summary>
    /// 根据记忆 ID 获取特定的长期记忆。
    /// 实现必须保证只能获取到归属于当前请求 userId 的记忆，防止越权读取。
    /// </summary>
    Task<Memory?> GetAsync(string userId, string memoryId);

    /// <summary>
    /// 获取归属于当前用户的所有长期记忆列表。支持可选的类别和状态过滤。
    /// </summary>
    Task<IReadOnlyList<Memory>> ListByUserAsync(string userId, string? type = null, string? status = null);

    /// <summary>
    /// 更新已有的长期记忆。仅允许修改 Content, Importance, ExpiresAt, Metadata 及系统元数据计数。
    /// 同样需校验 UserId 归属权并覆盖更新。
    /// </summary>
    Task<Memory> UpdateAsync(string userId, Memory memory);

    /// <summary>
    /// 快速归档一条记忆，将它的 status 修改为 archived，并同步更新 updatedAt。
    /// </summary>
    Task<Memory> ArchiveAsync(string userId, string memoryId);
}
