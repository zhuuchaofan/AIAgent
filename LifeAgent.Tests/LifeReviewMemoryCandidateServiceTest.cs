using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Tests;

public class LifeReviewMemoryCandidateServiceTest
{
    [Fact]
    public async Task KeepFromReviewCardAsync_WritesReviewStateWithoutMemoryWrite()
    {
        var stateStore = new FakeMemoryReviewStateStore();
        var service = new LifeReviewMemoryCandidateService(
            new FakeLifeEventService(new[]
            {
                new LifeEvent
                {
                    Id = "evt_1",
                    UserId = "user_a",
                    Title = "骑车回来",
                    Content = "今天骑车回来，心率不高。",
                    CreatedAt = DateTime.UtcNow,
                    OccurredAt = DateTime.UtcNow
                },
                new LifeEvent
                {
                    Id = "evt_2",
                    UserId = "user_a",
                    Title = "买运动服饰",
                    Content = "关注户外运动服饰，也会权衡价格。",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OccurredAt = DateTime.UtcNow.AddHours(-1)
                }
            }),
            stateStore);

        var response = await service.KeepFromReviewCardAsync("user_a", new LifeReviewKeepRequest
        {
            CardId = "worth_noticing",
            Title = "可能值得留意",
            Text = "你最近在关注运动服饰，也会权衡价格。",
            SourceEventIds = new[] { "evt_1", "evt_2", "evt_missing" }
        });

        Assert.True(response.Success);
        Assert.True(response.PreviewOnly);
        Assert.False(response.MemoryWriteEnabled);
        Assert.False(response.WroteMemory);
        Assert.True(response.WroteReviewState);
        Assert.Equal("kept", response.Data.ReviewStatus);
        Assert.Equal("preference", response.Data.Type);
        Assert.Equal(new[] { "evt_1", "evt_2" }, response.Data.SourceEventIds);
        Assert.NotNull(stateStore.Get(response.Data.Id));
    }

    [Fact]
    public async Task KeepFromReviewCardAsync_RejectsCardWithoutSources()
    {
        var service = new LifeReviewMemoryCandidateService(
            new FakeLifeEventService(Array.Empty<LifeEvent>()),
            new FakeMemoryReviewStateStore());

        await Assert.ThrowsAsync<InvalidInputException>(() => service.KeepFromReviewCardAsync(
            "user_a",
            new LifeReviewKeepRequest
            {
                CardId = "recent_state",
                Title = "最近状态",
                Text = "今天还没有足够记录可以回顾。",
                SourceEventIds = Array.Empty<string>()
            }));
    }

    private sealed class FakeMemoryReviewStateStore : IMemoryReviewStateStore
    {
        private readonly Dictionary<string, MemoryReviewCandidateItem> _candidates = new(StringComparer.OrdinalIgnoreCase);

        public MemoryReviewCandidateItem? Get(string candidateId)
        {
            return _candidates.GetValueOrDefault(candidateId);
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
