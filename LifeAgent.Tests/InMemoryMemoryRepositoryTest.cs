using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Xunit;

namespace LifeAgent.Tests;

/// <summary>
/// 长期记忆内存仓储 (InMemoryMemoryRepository) 单元测试。
/// 验证多用户权限隔离、Id 命名规范、更新限制以及归档生命周期。
/// </summary>
public class InMemoryMemoryRepositoryTest
{
    private readonly InMemoryMemoryRepository _repo = new();

    private Memory CreateMockMemory(string content = "偏好喝无糖燕麦奶咖啡。")
    {
        return new Memory
        {
            Type = "preference",
            Status = "active",
            Content = content,
            Importance = 3
        };
    }

    [Fact]
    public async Task CreateAsync_ForcesMemPrefixAndCorrectUserSetup()
    {
        var mockMemory = CreateMockMemory();
        var created = await _repo.CreateAsync("user_alice", mockMemory);

        Assert.NotNull(created);
        Assert.StartsWith("mem_", created.Id); // 强制 mem_ 前缀，杜绝 evt_ 混淆
        Assert.Equal("user_alice", created.UserId); // 绑定正确用户
        Assert.Equal(0, created.RecCount);
        Assert.Null(created.LastRecalledAt);
        Assert.Null(created.UpdatedAt);
        Assert.True(created.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task Repository_EnforcesStrongUserIsolation()
    {
        var aliceMemory = CreateMockMemory("Alice 的偏好");
        var bobMemory = CreateMockMemory("Bob 的偏好");

        var createdAlice = await _repo.CreateAsync("user_alice", aliceMemory);
        var createdBob = await _repo.CreateAsync("user_bob", bobMemory);

        // 1. 验证 Alice 无法读取 Bob 的记忆
        var fetchedBobByAlice = await _repo.GetAsync("user_alice", createdBob.Id);
        Assert.Null(fetchedBobByAlice);

        // 2. 验证 Bob 无法读取 Alice 的记忆
        var fetchedAliceByBob = await _repo.GetAsync("user_bob", createdAlice.Id);
        Assert.Null(fetchedAliceByBob);

        // 3. 验证列表查询按用户隔离
        var aliceList = await _repo.ListByUserAsync("user_alice");
        Assert.Single(aliceList);
        Assert.Equal(createdAlice.Id, aliceList[0].Id);

        var bobList = await _repo.ListByUserAsync("user_bob");
        Assert.Single(bobList);
        Assert.Equal(createdBob.Id, bobList[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_EnforcesUserIdAndCreatedAtImmutability()
    {
        var memory = CreateMockMemory("原始记忆");
        var created = await _repo.CreateAsync("user_alice", memory);

        var originalCreatedAt = created.CreatedAt;

        // 模拟篡改用户所有权和创建时间的非法更新
        var updatePayload = new Memory
        {
            Id = created.Id,
            UserId = "user_attacker", // 试图修改 UserId 归属
            Content = "修改后的内容",
            Importance = 4,
            Status = "active"
        };

        // 运行更新，传入的原鉴权上下文用户依然是 user_alice
        var updated = await _repo.UpdateAsync("user_alice", updatePayload);

        Assert.Equal("修改后的内容", updated.Content);
        Assert.Equal(4, updated.Importance);
        Assert.Equal("user_alice", updated.UserId); // 验证 UserId 无法被篡改
        Assert.Equal(originalCreatedAt, updated.CreatedAt); // 验证 CreatedAt 无法被篡改
        Assert.NotNull(updated.UpdatedAt); // 记录了更新时间
    }

    [Fact]
    public async Task UpdateAsync_RejectsCrossUserModification()
    {
        var memory = CreateMockMemory("原始记忆");
        var created = await _repo.CreateAsync("user_alice", memory);

        var updatePayload = new Memory
        {
            Id = created.Id,
            Content = "黑客篡改",
            Status = "active"
        };

        // Bob 试图传入 Alice 的 memory.Id 进行更新，期望抛出 KeyNotFoundException 越权拒绝
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.UpdateAsync("user_bob", updatePayload));
    }

    [Fact]
    public async Task ArchiveAsync_SuccessfullyTransitionsActiveToArchived()
    {
        var memory = CreateMockMemory();
        var created = await _repo.CreateAsync("user_alice", memory);
        Assert.Equal("active", created.Status);

        var archived = await _repo.ArchiveAsync("user_alice", created.Id);

        Assert.Equal("archived", archived.Status); // 状态自动流转为 archived
        Assert.NotNull(archived.UpdatedAt);

        // 验证 Get 依然能拿到归档状态的记忆
        var fetched = await _repo.GetAsync("user_alice", created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("archived", fetched.Status);
    }

    [Fact]
    public async Task ArchiveAsync_RejectsCrossUserArchiving()
    {
        var memory = CreateMockMemory();
        var created = await _repo.CreateAsync("user_alice", memory);

        // Bob 试图归档 Alice 的记忆，期望抛出 KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repo.ArchiveAsync("user_bob", created.Id));
    }
}
