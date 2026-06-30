using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Xunit;

namespace LifeAgent.Tests;

public class MemoryExtractionServiceTest
{
    private readonly MemoryExtractionService _service = new(new MemoryProposalGuard());

    [Fact]
    public void Extract_RequiresUserId()
    {
        Assert.Throws<ArgumentException>(() => _service.Extract(new MemoryExtractionRequest()));
    }

    [Fact]
    public void Extract_TimelineLikeInputGeneratesPreviewOnlyProposal()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            TimelineItems = new[]
            {
                new TimelineMemoryExtractionInput
                {
                    EventId = "evt_local_1",
                    Title = "偏好记录",
                    Content = "我喜欢 morning coffee"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("proposed", result[0].Status);
        Assert.Equal("timeline", result[0].SourceKind);
        Assert.NotNull(result[0].Proposal);
        var proposal = result[0].Proposal!;
        Assert.Equal("preference", proposal.MemoryType);
        Assert.True(proposal.PreviewOnly);
        Assert.Equal("phase6_5_local_extraction", proposal.Source);
    }

    [Fact]
    public void Extract_DailySummaryLikeInputGeneratesPreviewOnlyProposal()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_1",
                    Content = "我的目标 learn guitar"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("proposed", result[0].Status);
        Assert.Equal("summary", result[0].SourceKind);
        Assert.NotNull(result[0].Proposal);
        var proposal = result[0].Proposal!;
        Assert.Equal("goal", proposal.MemoryType);
        Assert.True(proposal.PreviewOnly);
    }

    [Fact]
    public void Extract_TrivialTimelineEventDoesNotGenerateLongTermMemory()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            TimelineItems = new[]
            {
                new TimelineMemoryExtractionInput
                {
                    EventId = "evt_trivial",
                    Content = "今天买了咖啡"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("skipped", result[0].Status);
        Assert.Null(result[0].Proposal);
        Assert.Equal("trivial_timeline_event", result[0].Reason);
    }

    [Fact]
    public void Extract_TemporaryEmotionalComplaintDoesNotGenerateRelationshipMemory()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_complaint",
                    Content = "今天很烦，临时抱怨朋友"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("skipped", result[0].Status);
        Assert.Null(result[0].Proposal);
        Assert.Equal("temporary_emotional_complaint", result[0].Reason);
    }

    [Fact]
    public void Extract_SensitiveMetadataIsRejected()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_sensitive",
                    Content = "我喜欢 coffee",
                    Metadata = new Dictionary<string, object>
                    {
                        ["api_key"] = "secret"
                    }
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("rejected", result[0].Status);
        Assert.True(result[0].GuardDecision!.Blocked);
    }

    [Fact]
    public void Extract_RawPayloadMetadataIsRejected()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_raw",
                    Content = "我喜欢 coffee",
                    Metadata = new Dictionary<string, object>
                    {
                        ["payload"] = "raw full payload"
                    }
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("rejected", result[0].Status);
        Assert.True(result[0].GuardDecision!.Blocked);
    }

    [Fact]
    public void Extract_LowConfidenceReturnsReviewRequired()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_low_confidence",
                    Content = "可能 我喜欢 coffee"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("review_required", result[0].Status);
        var decision = result[0].GuardDecision!;
        Assert.True(decision.ReviewRequired);
        Assert.Equal("low_confidence", decision.Reason);
    }

    [Fact]
    public void Extract_ConstraintProposalUsesImportanceFive()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_constraint",
                    Content = "我对 peanut 过敏，禁止 peanut"
                }
            }
        });

        Assert.Single(result);
        Assert.NotNull(result[0].Proposal);
        var proposal = result[0].Proposal!;
        Assert.Equal("constraint", proposal.MemoryType);
        Assert.Equal(5, proposal.Importance);
    }

    [Fact]
    public void Extract_TemporaryContextWithoutExpiresAtIsRejected()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            TimelineItems = new[]
            {
                new TimelineMemoryExtractionInput
                {
                    EventId = "evt_temp",
                    Content = "本周出差，缺少过期时间"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("rejected", result[0].Status);
        Assert.NotNull(result[0].Proposal);
        var proposal = result[0].Proposal!;
        Assert.Equal("temporary_context", proposal.MemoryType);
        Assert.Null(proposal.ExpiresAt);
    }

    [Fact]
    public void Extract_DoesNotWriteMemoryStoreOrLifeEvents()
    {
        var result = _service.Extract(new MemoryExtractionRequest
        {
            UserId = "user_a",
            Summaries = new[]
            {
                new SummaryMemoryExtractionInput
                {
                    SummaryId = "summary_write_check",
                    Content = "我习惯 every morning coding"
                }
            }
        });

        Assert.Single(result);
        Assert.Equal("proposed", result[0].Status);
        Assert.NotNull(result[0].Proposal);
        var proposal = result[0].Proposal!;
        Assert.True(proposal.PreviewOnly);
        Assert.Equal("phase6_5_local_extraction", proposal.Source);
        Assert.Equal("preview_proposal_generated", result[0].Reason);
    }
}
