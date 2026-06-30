using System.Text.RegularExpressions;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryProposalGuard : IMemoryProposalGuard
{
    private const double DuplicateThreshold = 0.6;
    private const double LowConfidenceThreshold = 0.35;
    private const double ConstraintReviewConfidenceThreshold = 0.75;

    private static readonly Regex SeparatorRegex = new(@"[\s,，。.!！?？;；:：、]+", RegexOptions.Compiled);
    private static readonly string[] PositivePreferenceMarkers = { "喜欢", "偏好", "想要", "prefer", "like" };
    private static readonly string[] NegativePreferenceMarkers = { "不喜欢", "讨厌", "不要", "禁止", "avoid", "dislike", "do not like" };
    private static readonly string[] HostileMarkers = { "愚蠢", "没用", "懒惰", "讨厌的人", "bad person", "stupid", "useless" };

    public MemoryPollutionDecision Evaluate(
        MemoryPreviewActionPayload proposal,
        IReadOnlyList<Memory> existingMemories)
    {
        if (proposal is null)
        {
            throw new ArgumentNullException(nameof(proposal));
        }

        existingMemories ??= Array.Empty<Memory>();

        var validationDecision = ValidateProposal(proposal);
        if (validationDecision is not null)
        {
            return validationDecision;
        }

        if (ContainsHostileInference(proposal.Content))
        {
            return ReviewRequired("hostile_inferred_fact");
        }

        if (proposal.Confidence < LowConfidenceThreshold)
        {
            return ReviewRequired("low_confidence");
        }

        var constraintDecision = EvaluateConstraintRules(proposal, existingMemories);
        if (constraintDecision is not null)
        {
            return constraintDecision;
        }

        var conflict = FindConflict(proposal, existingMemories);
        if (conflict.HasConflict)
        {
            return new MemoryPollutionDecision
            {
                Action = "review_required",
                ReviewRequired = true,
                Reason = "conflict_detected",
                ConflictResult = conflict
            };
        }

        var mergeCandidate = FindMergeCandidate(proposal, existingMemories);
        if (mergeCandidate.HasCandidate)
        {
            return new MemoryPollutionDecision
            {
                Action = "merge_candidate",
                ReviewRequired = true,
                Reason = "duplicate_like_proposal",
                MergeCandidate = mergeCandidate
            };
        }

        return new MemoryPollutionDecision
        {
            Action = "allow",
            Reason = "no_local_guard_issue"
        };
    }

    private static MemoryPollutionDecision? ValidateProposal(MemoryPreviewActionPayload proposal)
    {
        try
        {
            MemoryValidator.Validate(new Memory
            {
                UserId = "proposal_guard_user",
                Type = proposal.MemoryType,
                Status = MemoryStatus.PendingConfirm.ToSnakeCaseString(),
                Content = proposal.Content,
                Confidence = proposal.Confidence,
                Importance = proposal.Importance,
                Source = proposal.Source,
                ExpiresAt = proposal.ExpiresAt,
                Metadata = proposal.Metadata
            });
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Block("validator_rejected", ex.Message);
        }

        return null;
    }

    private static MemoryPollutionDecision? EvaluateConstraintRules(
        MemoryPreviewActionPayload proposal,
        IReadOnlyList<Memory> existingMemories)
    {
        if (!string.Equals(proposal.MemoryType, MemoryType.Constraint.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (proposal.Importance != 5)
        {
            return Block("constraint_requires_importance_5", "constraint proposals must use importance=5");
        }

        var conflict = FindConflict(proposal, existingMemories);
        if (conflict.HasConflict)
        {
            return new MemoryPollutionDecision
            {
                Action = "review_required",
                ReviewRequired = true,
                Reason = "constraint_conflict_requires_review",
                ConflictResult = conflict
            };
        }

        if (proposal.Confidence < ConstraintReviewConfidenceThreshold)
        {
            return ReviewRequired("constraint_low_confidence");
        }

        return null;
    }

    private static MemoryMergeCandidate FindMergeCandidate(
        MemoryPreviewActionPayload proposal,
        IReadOnlyList<Memory> existingMemories)
    {
        return existingMemories
            .Where(memory => string.Equals(memory.Type, proposal.MemoryType, StringComparison.OrdinalIgnoreCase))
            .Select(memory => new
            {
                Memory = memory,
                Similarity = CalculateTokenOverlap(proposal.Content, memory.Content)
            })
            .Where(candidate => candidate.Similarity >= DuplicateThreshold)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Memory.Id, StringComparer.Ordinal)
            .Select(candidate => new MemoryMergeCandidate
            {
                HasCandidate = true,
                ExistingMemoryId = candidate.Memory.Id,
                MemoryType = candidate.Memory.Type,
                SimilarityScore = Math.Round(candidate.Similarity, 4),
                Reason = "same_type_content_overlap"
            })
            .FirstOrDefault() ?? new MemoryMergeCandidate { MemoryType = proposal.MemoryType };
    }

    private static MemoryConflictResult FindConflict(
        MemoryPreviewActionPayload proposal,
        IReadOnlyList<Memory> existingMemories)
    {
        foreach (var memory in existingMemories.OrderBy(memory => memory.Id, StringComparer.Ordinal))
        {
            if (!AreRelatedTypes(proposal.MemoryType, memory.Type))
            {
                continue;
            }

            if (HasPreferencePolarityConflict(proposal.Content, memory.Content))
            {
                return new MemoryConflictResult
                {
                    HasConflict = true,
                    ExistingMemoryId = memory.Id,
                    MemoryType = memory.Type,
                    ConflictKind = "preference_polarity",
                    Reason = "positive_negative_preference_markers"
                };
            }
        }

        return new MemoryConflictResult { MemoryType = proposal.MemoryType };
    }

    private static bool AreRelatedTypes(string proposalType, string existingType)
    {
        if (string.Equals(proposalType, existingType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsPreferenceLike(proposalType) && IsPreferenceLike(existingType);
    }

    private static bool IsPreferenceLike(string type)
    {
        return string.Equals(type, MemoryType.Preference.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, MemoryType.Constraint.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPreferencePolarityConflict(string proposalContent, string existingContent)
    {
        var proposalPositive = ContainsAny(proposalContent, PositivePreferenceMarkers);
        var proposalNegative = ContainsAny(proposalContent, NegativePreferenceMarkers);
        var existingPositive = ContainsAny(existingContent, PositivePreferenceMarkers);
        var existingNegative = ContainsAny(existingContent, NegativePreferenceMarkers);
        if (!(proposalPositive && existingNegative || proposalNegative && existingPositive))
        {
            return false;
        }

        return CalculateTokenOverlap(proposalContent, existingContent) >= 0.2;
    }

    private static bool ContainsHostileInference(string content)
    {
        return ContainsAny(content, HostileMarkers);
    }

    private static bool ContainsAny(string content, IReadOnlyList<string> markers)
    {
        return markers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static double CalculateTokenOverlap(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string content)
    {
        return SeparatorRegex
            .Split(content)
            .Select(NormalizeToken)
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string token)
    {
        return token
            .Trim()
            .Trim('“', '”', '"', '\'', '。', '.', ',', '，', '！', '!', '?', '？')
            .ToLowerInvariant();
    }

    private static MemoryPollutionDecision Block(string reason, string detail)
    {
        return new MemoryPollutionDecision
        {
            Action = "block",
            Blocked = true,
            Reason = $"{reason}:{detail}"
        };
    }

    private static MemoryPollutionDecision ReviewRequired(string reason)
    {
        return new MemoryPollutionDecision
        {
            Action = "review_required",
            ReviewRequired = true,
            Reason = reason
        };
    }
}
