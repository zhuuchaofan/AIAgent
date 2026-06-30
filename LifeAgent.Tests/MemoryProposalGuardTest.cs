using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Xunit;

namespace LifeAgent.Tests;

public class MemoryProposalGuardTest
{
    private readonly MemoryProposalGuard _guard = new();

    [Fact]
    public void Evaluate_DuplicateLikeProposalProducesMergeCandidate()
    {
        var existing = ExistingMemory("mem_existing", "preference", "喜欢 coffee morning coding");
        var proposal = Proposal("preference", "喜欢 coffee morning coding");

        var decision = _guard.Evaluate(proposal, new[] { existing });

        Assert.Equal("merge_candidate", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.False(decision.Blocked);
        Assert.NotNull(decision.MergeCandidate);
        Assert.True(decision.MergeCandidate!.HasCandidate);
        Assert.Equal(existing.Id, decision.MergeCandidate.ExistingMemoryId);
    }

    [Fact]
    public void Evaluate_DuplicateCandidateDoesNotMutateExistingMemory()
    {
        var existing = ExistingMemory("mem_existing", "preference", "喜欢 coffee morning coding");
        var originalContent = existing.Content;
        var originalImportance = existing.Importance;

        _ = _guard.Evaluate(Proposal("preference", "喜欢 coffee morning coding"), new[] { existing });

        Assert.Equal(originalContent, existing.Content);
        Assert.Equal(originalImportance, existing.Importance);
        Assert.Equal("active", existing.Status);
    }

    [Fact]
    public void Evaluate_ContradictoryProposalProducesConflictResult()
    {
        var existing = ExistingMemory("mem_existing", "preference", "喜欢 coffee");
        var proposal = Proposal("preference", "不喜欢 coffee");

        var decision = _guard.Evaluate(proposal, new[] { existing });

        Assert.Equal("review_required", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.NotNull(decision.ConflictResult);
        Assert.True(decision.ConflictResult!.HasConflict);
        Assert.Equal("preference_polarity", decision.ConflictResult.ConflictKind);
        Assert.Equal(existing.Id, decision.ConflictResult.ExistingMemoryId);
    }

    [Fact]
    public void Evaluate_BlocksSensitiveMetadata()
    {
        var proposal = Proposal("preference", "喜欢 coffee");
        proposal.Metadata = new Dictionary<string, object>
        {
            ["api_key"] = "secret"
        };

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("block", decision.Action);
        Assert.True(decision.Blocked);
        Assert.Contains("validator_rejected", decision.Reason);
    }

    [Fact]
    public void Evaluate_BlocksRawPayloadMetadata()
    {
        var proposal = Proposal("preference", "喜欢 coffee");
        proposal.Metadata = new Dictionary<string, object>
        {
            ["payload"] = new string('x', 100)
        };

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("block", decision.Action);
        Assert.True(decision.Blocked);
        Assert.Contains("validator_rejected", decision.Reason);
    }

    [Fact]
    public void Evaluate_LowConfidenceProposalRequiresReview()
    {
        var proposal = Proposal("preference", "喜欢 coffee", confidence: 0.2);

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("review_required", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.Equal("low_confidence", decision.Reason);
    }

    [Fact]
    public void Evaluate_HostileInferredFactRequiresReview()
    {
        var proposal = Proposal("person", "张三 是 没用 的 人", confidence: 0.9);

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("review_required", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.Equal("hostile_inferred_fact", decision.Reason);
    }

    [Fact]
    public void Evaluate_ConstraintImportanceBelowFiveIsBlocked()
    {
        var proposal = Proposal("constraint", "禁止 peanut", confidence: 0.95, importance: 4);

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("block", decision.Action);
        Assert.True(decision.Blocked);
        Assert.Contains("validator_rejected", decision.Reason);
    }

    [Fact]
    public void Evaluate_ConstraintLowConfidenceRequiresReview()
    {
        var proposal = Proposal("constraint", "禁止 peanut", confidence: 0.6, importance: 5);

        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());

        Assert.Equal("review_required", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.Equal("constraint_low_confidence", decision.Reason);
    }

    [Fact]
    public void Evaluate_ConstraintConflictRequiresReview()
    {
        var existing = ExistingMemory("mem_existing", "preference", "喜欢 peanut");
        var proposal = Proposal("constraint", "禁止 peanut", confidence: 0.95, importance: 5);

        var decision = _guard.Evaluate(proposal, new[] { existing });

        Assert.Equal("review_required", decision.Action);
        Assert.True(decision.ReviewRequired);
        Assert.NotNull(decision.ConflictResult);
        Assert.True(decision.ConflictResult!.HasConflict);
        Assert.Equal("constraint_conflict_requires_review", decision.Reason);
    }

    [Fact]
    public void Evaluate_AllowsCleanProposalWithoutMutation()
    {
        var existing = ExistingMemory("mem_existing", "preference", "喜欢 tea evening");
        var proposal = Proposal("goal", "目标 learn guitar", confidence: 0.8, importance: 3);

        var decision = _guard.Evaluate(proposal, new[] { existing });

        Assert.Equal("allow", decision.Action);
        Assert.False(decision.Blocked);
        Assert.False(decision.ReviewRequired);
        Assert.Equal("喜欢 tea evening", existing.Content);
        Assert.Equal("active", existing.Status);
    }

    private static MemoryPreviewActionPayload Proposal(
        string memoryType,
        string content,
        double confidence = 0.8,
        int importance = 3)
    {
        return new MemoryPreviewActionPayload
        {
            MemoryType = memoryType,
            Content = content,
            Confidence = confidence,
            Importance = importance,
            Source = "agent_preview",
            PreviewOnly = true,
            OriginalMessage = content
        };
    }

    private static Memory ExistingMemory(string id, string type, string content)
    {
        return new Memory
        {
            Id = id,
            UserId = "user_a",
            Type = type,
            Status = "active",
            Content = content,
            Confidence = 0.8,
            Importance = 3,
            Source = "test",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };
    }
}
