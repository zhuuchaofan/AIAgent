using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public interface IMemoryReviewRememberService
{
    Task<MemoryReviewCandidateActionResponse> RememberAsync(
        string userId,
        string candidateId,
        MemoryReviewRememberRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryReviewRememberService : IMemoryReviewRememberService
{
    private const int DefaultImportance = 3;
    private const int MaxContentLength = 500;
    private readonly IMemoryReviewStateStore _reviewStateStore;
    private readonly IMemoryRepository _memoryRepository;
    private readonly IMemoryProposalGuard _memoryProposalGuard;
    private readonly TimeProvider _timeProvider;

    public MemoryReviewRememberService(
        IMemoryReviewStateStore reviewStateStore,
        IMemoryRepository memoryRepository,
        IMemoryProposalGuard memoryProposalGuard,
        TimeProvider timeProvider)
    {
        _reviewStateStore = reviewStateStore;
        _memoryRepository = memoryRepository;
        _memoryProposalGuard = memoryProposalGuard;
        _timeProvider = timeProvider;
    }

    public async Task<MemoryReviewCandidateActionResponse> RememberAsync(
        string userId,
        string candidateId,
        MemoryReviewRememberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException();
        }

        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new InvalidInputException("记忆候选 id 不能为空。");
        }

        if (request == null)
        {
            throw new InvalidInputException("请先确认要记住的内容。");
        }

        var content = NormalizeContent(request.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidInputException("请先确认要记住的内容。");
        }

        if (content.Length > MaxContentLength)
        {
            throw new InvalidInputException($"记忆内容不能超过 {MaxContentLength} 个字符。");
        }

        var candidate = await _reviewStateStore.GetCandidateAsync(userId, candidateId, cancellationToken);
        if (candidate == null)
        {
            throw new InvalidInputException("这条线索还没有留在收件箱里，不能直接记住。");
        }

        if (string.Equals(candidate.ReviewStatus, "remembered", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("这条线索已经记住了。");
        }

        if (!string.Equals(candidate.ReviewStatus, "kept", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("只有已留着的线索才能被记住。");
        }

        var memoryType = NormalizeMemoryType(candidate.Type);
        var importance = Math.Clamp(request.Importance ?? DefaultImportance, 1, 5);
        var expiresAt = string.Equals(memoryType, MemoryType.TemporaryContext.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase)
            ? _timeProvider.GetUtcNow().UtcDateTime.AddDays(30)
            : (DateTime?)null;

        var proposal = new MemoryPreviewActionPayload
        {
            MemoryType = memoryType,
            Content = content,
            Confidence = candidate.Confidence > 0 ? candidate.Confidence : 0.8,
            Importance = importance,
            Source = "memory_review_confirmed",
            OriginalMessage = candidate.Title,
            SourceText = candidate.Detail,
            ExpiresAt = expiresAt,
            Metadata = new Dictionary<string, object>
            {
                ["reviewCandidateId"] = candidate.Id,
                ["reviewStage"] = candidate.ReviewStage,
                ["sourceEventIds"] = candidate.SourceEventIds.ToArray()
            }
        };

        var existingMemories = await _memoryRepository.ListByUserAsync(
            userId,
            type: null,
            status: MemoryStatus.Active.ToSnakeCaseString());
        var guardDecision = _memoryProposalGuard.Evaluate(proposal, existingMemories);
        if (guardDecision.Blocked)
        {
            throw new InvalidInputException($"这条记忆暂时不能写入：{guardDecision.Reason}");
        }

        if (!string.Equals(guardDecision.Action, "allow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("这条记忆需要进一步确认，暂不写入。");
        }

        var memory = await _memoryRepository.CreateAsync(userId, new Memory
        {
            Type = memoryType,
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = content,
            Confidence = proposal.Confidence,
            Importance = proposal.Importance,
            Source = "memory_review_confirmed",
            SourceEventIds = candidate.SourceEventIds.ToList(),
            ExpiresAt = proposal.ExpiresAt,
            Metadata = proposal.Metadata
        });

        candidate.Title = content;
        candidate.ReviewStatus = "remembered";
        candidate.MemoryId = memory.Id;
        var state = await _reviewStateStore.UpsertAsync(
            userId,
            new MemoryReviewStateUpsertRequest(candidate, "remembered", memory.Id),
            cancellationToken);

        var updatedCandidate = MemoryReviewInboxStateProjection.ApplyState(
            candidate,
            new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
            {
                [candidate.Id] = state
            });

        return new MemoryReviewCandidateActionResponse
        {
            Success = true,
            PreviewOnly = false,
            MemoryWriteEnabled = true,
            WroteMemory = true,
            WroteReviewState = true,
            MemoryId = memory.Id,
            Data = updatedCandidate
        };
    }

    private static string NormalizeContent(string? content)
    {
        return string.Join(
            " ",
            (content ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string NormalizeMemoryType(string candidateType)
    {
        if (MemoryTypeHelper.IsValid(candidateType))
        {
            return candidateType;
        }

        return MemoryType.Theme.ToSnakeCaseString();
    }
}
