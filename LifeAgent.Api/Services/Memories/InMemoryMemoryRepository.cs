using System.Collections.Concurrent;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

/// <summary>
/// 长期记忆的内存仓储实现（Fake Repository），仅用于本地测试与仿真验证。
/// 架构设计说明：
/// 1. 数据物理隔离：采用双层嵌套 ConcurrentDictionary，外层 Key 强制为 userId，从物理层确保绝对无法跨用户越权访问。
/// 2. 不做持久化与依赖：本实现不接任何真实 Firestore，不注册到 Program.cs 的 DI 容器。
/// 3. 创建幂等性：当前 6.1 阶段按规范暂不强制实现分布式幂等创建，但会在元数据与逻辑中留空记录。
/// </summary>
public class InMemoryMemoryRepository : IMemoryRepository
{
    // 双层线程安全字典：外层 Key: userId, 内层 Key: memoryId
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Memory>> _store = new();

    /// <inheritdoc/>
    public Task<Memory> CreateAsync(string userId, Memory memory)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空，必须由认证上下文注入", nameof(userId));

        if (memory == null)
            throw new ArgumentNullException(nameof(memory));

        // 命名强制使用 mem_ 前缀，避免与 Timeline (evt_) 混淆
        var memoryId = $"mem_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        memory.Id = memoryId;
        memory.UserId = userId;
        memory.CreatedAt = now;
        memory.UpdatedAt = null;
        memory.LastRecalledAt = null;
        memory.RecCount = 0;

        // 强制进行用户隔离存储
        var userDict = _store.GetOrAdd(userId, _ => new ConcurrentDictionary<string, Memory>());

        // 幂等性记录：当前阶段直接进行 TryAdd 写入，后续可扩展对特定 payload hash 的幂等去重检查
        if (!userDict.TryAdd(memoryId, memory))
        {
            throw new InvalidOperationException($"记忆创建失败，ID {memoryId} 已存在 (可能发生了 Guid 碰撞)");
        }

        return Task.FromResult(memory);
    }

    /// <inheritdoc/>
    public Task<Memory?> GetAsync(string userId, string memoryId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        if (string.IsNullOrWhiteSpace(memoryId))
            return Task.FromResult<Memory?>(null);

        // 如果用户字典不存在，返回 null，防止越权读取
        if (!_store.TryGetValue(userId, out var userDict))
        {
            return Task.FromResult<Memory?>(null);
        }

        userDict.TryGetValue(memoryId, out var memory);
        return Task.FromResult(memory);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Memory>> ListByUserAsync(string userId, string? type = null, string? status = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        if (!_store.TryGetValue(userId, out var userDict))
        {
            return Task.FromResult<IReadOnlyList<Memory>>(Array.Empty<Memory>());
        }

        IEnumerable<Memory> query = userDict.Values;

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(m => string.Equals(m.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => string.Equals(m.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        IReadOnlyList<Memory> result = query.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Memory> UpdateAsync(string userId, Memory memory)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        if (memory == null)
            throw new ArgumentNullException(nameof(memory));

        if (string.IsNullOrWhiteSpace(memory.Id))
            throw new ArgumentException("待更新的 memory 必须包含合法的 Id", nameof(memory));

        if (!_store.TryGetValue(userId, out var userDict) || !userDict.TryGetValue(memory.Id, out var existing))
        {
            throw new KeyNotFoundException($"归属于用户 {userId} 的记忆 ID {memory.Id} 未找到，更新被拒绝。");
        }

        // 仅允许修改业务限制的可写字段与统计属性，保护 UserId / CreatedAt 不被篡改
        existing.Content = memory.Content;
        existing.Type = string.IsNullOrWhiteSpace(memory.Type) ? existing.Type : memory.Type;
        existing.Importance = memory.Importance;
        existing.ExpiresAt = memory.ExpiresAt;
        existing.Metadata = memory.Metadata;
        existing.Confidence = memory.Confidence;
        existing.Status = memory.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        MemoryValidator.Validate(existing);

        return Task.FromResult(existing);
    }

    /// <inheritdoc/>
    public async Task<Memory> ArchiveAsync(string userId, string memoryId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        var memory = await GetAsync(userId, memoryId);
        if (memory == null)
        {
            throw new KeyNotFoundException($"归属于用户 {userId} 的记忆 ID {memoryId} 未找到，归档被拒绝。");
        }

        // 将状态状态流转为 archived
        memory.Status = MemoryStatus.Archived.ToSnakeCaseString();
        memory.UpdatedAt = DateTime.UtcNow;

        return memory;
    }
}
