using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Xunit;

namespace LifeAgent.Tests;

public class MemoryRetrievalServiceTest
{
    private readonly InMemoryMemoryRepository _repository = new();
    private readonly InMemoryMemoryRetrievalService _retrieval;

    public MemoryRetrievalServiceTest()
    {
        _retrieval = new InMemoryMemoryRetrievalService(_repository);
    }

    [Fact]
    public async Task RetrieveAsync_RequiresUserId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _retrieval.RetrieveAsync(new MemoryRetrievalRequest()));
    }

    [Fact]
    public async Task RetrieveAsync_EnforcesUserIsolation()
    {
        var alice = await CreateMemory("user_alice", "Alice 喜欢早上喝咖啡");
        await CreateMemory("user_bob", "Bob 喜欢晚上喝茶");

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_alice",
            Query = "喜欢"
        });

        Assert.Single(results);
        Assert.Equal(alice.Id, results[0].MemoryId);
        Assert.DoesNotContain(results, result => result.Content.Contains("Bob", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_DefaultsToActiveMemoriesOnly()
    {
        var active = await CreateMemory("user_a", "active coffee preference", status: "active");
        await CreateMemory("user_a", "pending coffee proposal", status: "pending_confirm");
        await CreateMemory("user_a", "archived coffee memory", status: "archived");

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "coffee"
        });

        Assert.Single(results);
        Assert.Equal(active.Id, results[0].MemoryId);
    }

    [Fact]
    public async Task RetrieveAsync_ExcludesArchivedUnlessIncluded()
    {
        var active = await CreateMemory("user_a", "coffee active", status: "active");
        var archived = await CreateMemory("user_a", "coffee archived", status: "archived");

        var defaultResults = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "coffee",
            Statuses = new[] { "active", "archived" }
        });
        var withArchived = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "coffee",
            Statuses = new[] { "active", "archived" },
            IncludeArchived = true
        });

        Assert.Single(defaultResults);
        Assert.Equal(active.Id, defaultResults[0].MemoryId);
        Assert.Contains(withArchived, result => result.MemoryId == active.Id);
        Assert.Contains(withArchived, result => result.MemoryId == archived.Id);
    }

    [Fact]
    public async Task RetrieveAsync_ExcludesExpiredTemporaryContext()
    {
        var activeTemporary = await CreateMemory(
            "user_a",
            "深圳出差到周五",
            type: "temporary_context",
            expiresAt: DateTime.UtcNow.AddDays(1));
        await CreateMemory(
            "user_a",
            "上海出差已结束",
            type: "temporary_context",
            expiresAt: DateTime.UtcNow.AddDays(-1));

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "出差",
            Types = new[] { "temporary_context" }
        });

        Assert.Single(results);
        Assert.Equal(activeTemporary.Id, results[0].MemoryId);
    }

    [Fact]
    public async Task RetrieveAsync_AppliesTypeAndStatusFilters()
    {
        var activePreference = await CreateMemory("user_a", "喜欢咖啡", type: "preference", status: "active");
        await CreateMemory("user_a", "咖啡目标", type: "goal", status: "active");
        var pendingPreference = await CreateMemory("user_a", "待确认咖啡偏好", type: "preference", status: "pending_confirm");

        var typeResults = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "咖啡",
            Types = new[] { "preference" }
        });
        var statusResults = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "咖啡",
            Types = new[] { "preference" },
            Statuses = new[] { "pending_confirm" }
        });

        Assert.Single(typeResults);
        Assert.Equal(activePreference.Id, typeResults[0].MemoryId);
        Assert.Single(statusResults);
        Assert.Equal(pendingPreference.Id, statusResults[0].MemoryId);
    }

    [Fact]
    public async Task RetrieveAsync_QueryMayBeEmptyAndReturnsStableFilteredList()
    {
        var first = await CreateMemory("user_a", "第一条偏好", importance: 3);
        var second = await CreateMemory("user_a", "第二条偏好", importance: 5);

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Types = new[] { "preference" }
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(second.Id, results[0].MemoryId);
        Assert.Equal(first.Id, results[1].MemoryId);
    }

    [Fact]
    public async Task RetrieveAsync_RanksByContentImportanceConfidenceAndUpdatedAt()
    {
        var low = await CreateMemory("user_a", "咖啡", importance: 1, confidence: 0.1);
        var highImportance = await CreateMemory("user_a", "咖啡", importance: 5, confidence: 0.1);
        var highConfidence = await CreateMemory("user_a", "咖啡", importance: 5, confidence: 0.9);
        highConfidence.UpdatedAt = DateTime.UtcNow.AddMinutes(1);
        highImportance.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);
        low.UpdatedAt = DateTime.UtcNow.AddMinutes(-2);

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "咖啡"
        });

        Assert.Equal(highConfidence.Id, results[0].MemoryId);
        Assert.Equal(highImportance.Id, results[1].MemoryId);
        Assert.Equal(low.Id, results[2].MemoryId);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.Contains("content_match", results[0].Reason);
    }

    [Fact]
    public async Task RetrieveAsync_LimitIsApplied()
    {
        await CreateMemory("user_a", "coffee one", importance: 5);
        await CreateMemory("user_a", "coffee two", importance: 4);
        await CreateMemory("user_a", "coffee three", importance: 3);

        var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
        {
            UserId = "user_a",
            Query = "coffee",
            Limit = 2
        });

        Assert.Equal(2, results.Count);
    }

    private async Task<Memory> CreateMemory(
        string userId,
        string content,
        string type = "preference",
        string status = "active",
        int importance = 3,
        double confidence = 0.8,
        DateTime? expiresAt = null)
    {
        return await _repository.CreateAsync(userId, new Memory
        {
            Type = type,
            Status = status,
            Content = content,
            Importance = importance,
            Confidence = confidence,
            Source = "test",
            ExpiresAt = expiresAt
        });
    }
}
