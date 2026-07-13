using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Tests;

public class MemoryReviewRememberServiceTest
{
    [Fact]
    public async Task RememberAsync_CreatesActiveMemoryAndMarksCandidateRemembered()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        stateStore.Set(Candidate("review_body_movement", "kept", "habit", "你会关注运动状态和身体感受。"));
        var memoryRepository = new InMemoryMemoryRepository();
        var service = Service(stateStore, memoryRepository);

        var response = await service.RememberAsync(
            "user_a",
            "review_body_movement",
            new MemoryReviewRememberRequest
            {
                Content = "我会关注运动状态和身体感受。",
                Importance = 3
            });

        Assert.True(response.WroteMemory);
        Assert.False(response.PreviewOnly);
        Assert.True(response.MemoryWriteEnabled);
        Assert.Equal("remembered", response.Data.ReviewStatus);
        Assert.False(string.IsNullOrWhiteSpace(response.MemoryId));

        var memory = Assert.Single(await memoryRepository.ListByUserAsync("user_a", status: "active"));
        Assert.Equal(response.MemoryId, memory.Id);
        Assert.Equal("habit", memory.Type);
        Assert.Equal("我会关注运动状态和身体感受。", memory.Content);
        Assert.Equal(new[] { "evt_1" }, memory.SourceEventIds);
        Assert.Equal(response.MemoryId, stateStore.Get("review_body_movement")!.MemoryId);
    }

    [Fact]
    public async Task RememberAsync_RejectsCandidateThatWasNotKept()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        stateStore.Set(Candidate("review_pending", "pending", "theme", "你最近在整理项目。"));
        var service = Service(stateStore, new InMemoryMemoryRepository());

        await Assert.ThrowsAsync<InvalidInputException>(() => service.RememberAsync(
            "user_a",
            "review_pending",
            new MemoryReviewRememberRequest { Content = "我最近在整理项目。" }));
    }

    [Fact]
    public async Task RememberAsync_RejectsValidatorBlockedContent()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        stateStore.Set(Candidate("review_token", "kept", "theme", "需要记住 token。"));
        var service = Service(stateStore, new InMemoryMemoryRepository());

        await Assert.ThrowsAsync<InvalidInputException>(() => service.RememberAsync(
            "user_a",
            "review_token",
            new MemoryReviewRememberRequest
            {
                Content = "Authorization: Bearer mock_local_token_123"
            }));
    }

    [Fact]
    public async Task RememberAsync_RejectsMergeCandidateWithoutAutoMerge()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        stateStore.Set(Candidate("review_project", "kept", "theme", "我最近在持续整理 LifeOS 项目。"));
        var memoryRepository = new InMemoryMemoryRepository();
        await memoryRepository.CreateAsync("user_a", new Memory
        {
            Type = "theme",
            Status = "active",
            Content = "我最近在持续整理 LifeOS 项目。",
            Confidence = 0.9,
            Importance = 3,
            Source = "test"
        });
        var service = Service(stateStore, memoryRepository);

        await Assert.ThrowsAsync<InvalidInputException>(() => service.RememberAsync(
            "user_a",
            "review_project",
            new MemoryReviewRememberRequest { Content = "我最近在持续整理 LifeOS 项目。" }));
    }

    [Fact]
    public async Task RememberAsync_TemporaryContextGetsFutureExpiresAt()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        stateStore.Set(Candidate("review_xinjiang", "kept", "temporary_context", "我近期有去新疆的出行计划。"));
        var memoryRepository = new InMemoryMemoryRepository();
        var service = Service(stateStore, memoryRepository);

        await service.RememberAsync(
            "user_a",
            "review_xinjiang",
            new MemoryReviewRememberRequest { Content = "我近期有去新疆的出行计划。" });

        var memory = Assert.Single(await memoryRepository.ListByUserAsync("user_a", status: "active"));
        Assert.Equal("temporary_context", memory.Type);
        Assert.NotNull(memory.ExpiresAt);
        Assert.True(memory.ExpiresAt > DateTime.UtcNow.AddDays(20));
    }

    private static MemoryReviewRememberService Service(
        IMemoryReviewStateStore stateStore,
        IMemoryRepository memoryRepository)
    {
        return new MemoryReviewRememberService(
            stateStore,
            memoryRepository,
            new MemoryProposalGuard(),
            TimeProvider.System);
    }

    private static MemoryReviewCandidateItem Candidate(
        string id,
        string status,
        string type,
        string title)
    {
        return new MemoryReviewCandidateItem
        {
            Id = id,
            Type = type,
            Title = title,
            Detail = "来自最近生活记录。",
            ReviewStage = "stable",
            ReviewStageLabel = "更稳定",
            SourceEventIds = new[] { "evt_1" },
            Sources = new[]
            {
                new MemoryReviewSourceItem
                {
                    EventId = "evt_1",
                    Title = "生活记录",
                    Snippet = title,
                    OccurredAt = DateTime.UtcNow
                }
            },
            Confidence = 0.86,
            Reason = "最近多次出现",
            ReviewStatus = status
        };
    }

    private sealed class FakeMemoryReviewStateStore : IMemoryReviewStateStore
    {
        private readonly Dictionary<string, MemoryReviewCandidateItem> _candidates = new(StringComparer.OrdinalIgnoreCase);

        public MemoryReviewCandidateItem? Get(string candidateId)
        {
            return _candidates.GetValueOrDefault(candidateId);
        }

        public void Set(MemoryReviewCandidateItem candidate)
        {
            _candidates[candidate.Id] = candidate;
        }

        public Task<IReadOnlyDictionary<string, MemoryReviewStateRecord>> ListByCandidateIdsAsync(
            string userId,
            IReadOnlyList<string> candidateIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, MemoryReviewStateRecord> result = _candidates
                .Where(pair => candidateIds.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(
                    pair => pair.Key,
                    pair => new MemoryReviewStateRecord(
                        pair.Value.Id,
                        pair.Value.ReviewStatus,
                        DateTime.UtcNow,
                        pair.Value.ReviewedAt,
                        pair.Value.MemoryId),
                    StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<MemoryReviewCandidateItem>> ListKeptCandidatesAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MemoryReviewCandidateItem> result = _candidates.Values
                .Where(candidate => candidate.ReviewStatus is "kept" or "remembered")
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<MemoryReviewCandidateItem?> GetCandidateAsync(
            string userId,
            string candidateId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_candidates.GetValueOrDefault(candidateId));
        }

        public Task<MemoryReviewStateRecord> UpsertAsync(
            string userId,
            MemoryReviewStateUpsertRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Candidate.ReviewStatus = request.Status;
            request.Candidate.MemoryId = request.MemoryId;
            request.Candidate.ReviewedAt = DateTime.UtcNow;
            _candidates[request.Candidate.Id] = request.Candidate;
            return Task.FromResult(new MemoryReviewStateRecord(
                request.Candidate.Id,
                request.Status,
                DateTime.UtcNow,
                request.Candidate.ReviewedAt,
                request.MemoryId));
        }
    }
}
