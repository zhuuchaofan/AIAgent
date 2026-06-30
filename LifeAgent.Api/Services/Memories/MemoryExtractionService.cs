using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryExtractionService : IMemoryExtractionService
{
    private readonly IMemoryProposalGuard _guard;

    public MemoryExtractionService(IMemoryProposalGuard guard)
    {
        _guard = guard;
    }

    public IReadOnlyList<MemoryExtractionResult> Extract(MemoryExtractionRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("userId is required for memory extraction.", nameof(request));
        }

        var results = new List<MemoryExtractionResult>();
        foreach (var item in request.TimelineItems)
        {
            results.Add(EvaluateTimelineItem(item));
        }

        foreach (var summary in request.Summaries)
        {
            results.Add(EvaluateSummary(summary));
        }

        return results.AsReadOnly();
    }

    private MemoryExtractionResult EvaluateTimelineItem(TimelineMemoryExtractionInput input)
    {
        var sourceId = string.IsNullOrWhiteSpace(input.EventId) ? "timeline_local" : input.EventId;
        var text = $"{input.Title} {input.Content}".Trim();
        if (IsTrivial(text))
        {
            return Skipped("timeline", sourceId, "trivial_timeline_event");
        }

        var proposal = BuildProposal(text, sourceText: input.Content, input.Metadata, sourceId);
        if (proposal is null)
        {
            return Skipped("timeline", sourceId, "no_stable_memory_signal");
        }

        return ValidateAndGuard("timeline", sourceId, proposal);
    }

    private MemoryExtractionResult EvaluateSummary(SummaryMemoryExtractionInput input)
    {
        var sourceId = string.IsNullOrWhiteSpace(input.SummaryId) ? "summary_local" : input.SummaryId;
        var text = input.Content.Trim();
        if (IsTemporaryComplaint(text))
        {
            return Skipped("summary", sourceId, "temporary_emotional_complaint");
        }

        var proposal = BuildProposal(text, sourceText: input.Content, input.Metadata, sourceId);
        if (proposal is null)
        {
            return Skipped("summary", sourceId, "no_stable_memory_signal");
        }

        return ValidateAndGuard("summary", sourceId, proposal);
    }

    private MemoryExtractionResult ValidateAndGuard(
        string sourceKind,
        string sourceId,
        MemoryPreviewActionPayload proposal)
    {
        var decision = _guard.Evaluate(proposal, Array.Empty<Memory>());
        if (decision.Blocked)
        {
            return new MemoryExtractionResult
            {
                Status = "rejected",
                SourceKind = sourceKind,
                SourceId = sourceId,
                Reason = decision.Reason,
                Proposal = proposal,
                GuardDecision = decision
            };
        }

        if (decision.ReviewRequired)
        {
            return new MemoryExtractionResult
            {
                Status = "review_required",
                SourceKind = sourceKind,
                SourceId = sourceId,
                Reason = decision.Reason,
                Proposal = proposal,
                GuardDecision = decision
            };
        }

        return new MemoryExtractionResult
        {
            Status = "proposed",
            SourceKind = sourceKind,
            SourceId = sourceId,
            Reason = "preview_proposal_generated",
            Proposal = proposal,
            GuardDecision = decision
        };
    }

    private static MemoryExtractionResult Skipped(string sourceKind, string sourceId, string reason)
    {
        return new MemoryExtractionResult
        {
            Status = "skipped",
            SourceKind = sourceKind,
            SourceId = sourceId,
            Reason = reason
        };
    }

    private static MemoryPreviewActionPayload? BuildProposal(
        string text,
        string sourceText,
        Dictionary<string, object>? metadata,
        string sourceId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ContainsAny(text, "出差", "临时", "本周", "这周"))
        {
            return new MemoryPreviewActionPayload
            {
                MemoryType = MemoryType.TemporaryContext.ToSnakeCaseString(),
                Content = text,
                Confidence = InferConfidence(text),
                Importance = 2,
                Source = "phase6_5_local_extraction",
                PreviewOnly = true,
                OriginalMessage = text,
                SourceText = sourceText,
                Metadata = BuildMetadata(metadata, sourceId),
                ExpiresAt = InferTemporaryExpiry(text)
            };
        }

        if (ContainsAny(text, "限制", "禁止", "不要", "过敏", "不能"))
        {
            return Proposal(MemoryType.Constraint.ToSnakeCaseString(), text, sourceText, metadata, sourceId, importance: 5);
        }

        if (ContainsAny(text, "目标", "我要", "我以后要", "计划"))
        {
            return Proposal(MemoryType.Goal.ToSnakeCaseString(), text, sourceText, metadata, sourceId);
        }

        if (ContainsAny(text, "习惯", "每天", "每周", "总是"))
        {
            return Proposal(MemoryType.Habit.ToSnakeCaseString(), text, sourceText, metadata, sourceId);
        }

        if (ContainsAny(text, "项目", "project"))
        {
            return Proposal(MemoryType.Project.ToSnakeCaseString(), text, sourceText, metadata, sourceId);
        }

        if (ContainsAny(text, "我不喜欢", "我喜欢", "偏好"))
        {
            return Proposal(MemoryType.Preference.ToSnakeCaseString(), text, sourceText, metadata, sourceId);
        }

        if (ContainsAny(text, "伴侣", "朋友", "家人", "同事"))
        {
            return Proposal(MemoryType.Relationship.ToSnakeCaseString(), text, sourceText, metadata, sourceId);
        }

        return null;
    }

    private static MemoryPreviewActionPayload Proposal(
        string memoryType,
        string text,
        string sourceText,
        Dictionary<string, object>? metadata,
        string sourceId,
        int importance = 3)
    {
        return new MemoryPreviewActionPayload
        {
            MemoryType = memoryType,
            Content = text,
            Confidence = InferConfidence(text),
            Importance = importance,
            Source = "phase6_5_local_extraction",
            PreviewOnly = true,
            OriginalMessage = text,
            SourceText = sourceText,
            Metadata = BuildMetadata(metadata, sourceId)
        };
    }

    private static double InferConfidence(string text)
    {
        return ContainsAny(text, "可能", "也许", "好像", "大概", "猜测") ? 0.3 : 0.82;
    }

    private static DateTime? InferTemporaryExpiry(string text)
    {
        return ContainsAny(text, "缺少过期时间", "无过期时间")
            ? null
            : DateTime.UtcNow.AddDays(7);
    }

    private static Dictionary<string, object>? BuildMetadata(
        Dictionary<string, object>? inputMetadata,
        string sourceId)
    {
        var metadata = inputMetadata is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(inputMetadata);
        metadata["sourceId"] = sourceId;
        metadata["extractionStage"] = "phase6_5_local_skeleton";
        return metadata;
    }

    private static bool IsTrivial(string text)
    {
        return ContainsAny(text, "买了咖啡", "吃了午饭", "散步", "看电影") &&
               !ContainsAny(text, "喜欢", "不喜欢", "目标", "习惯", "限制", "禁止", "过敏");
    }

    private static bool IsTemporaryComplaint(string text)
    {
        return ContainsAny(text, "今天很烦", "一时生气", "临时抱怨", "心情不好") &&
               !ContainsAny(text, "以后", "总是", "目标", "限制", "习惯");
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
