using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Api.Endpoints;

public static class MemoryInsightEndpoints
{
    public static void MapMemoryInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memory");

        group.MapGet("/insights/preview", async (
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IMemoryInsightPreviewService memoryInsightPreviewService,
            int limit = 20) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var boundedLimit = Math.Clamp(limit, 1, 50);
            var events = await lifeEventService.ListEventsAsync(
                userId,
                type: "all",
                limit: boundedLimit,
                cursor: null,
                tag: null);

            var preview = memoryInsightPreviewService.BuildPreview(userId, events.Data);

            return Results.Ok(new MemoryInsightPreviewResponse
            {
                Success = true,
                Data = preview
            });
        }).RequireRateLimiting("auth-user");

        group.MapGet("/review-inbox/preview", async (
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
            IMemoryReviewStateStore memoryReviewStateStore,
            int limit = 20) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var preview = await BuildReviewPreviewAsync(
                userId,
                limit,
                lifeEventService,
                memoryReviewInboxPreviewService,
                memoryReviewStateStore);

            return Results.Ok(new MemoryReviewInboxPreviewResponse
            {
                Success = true,
                Data = preview
            });
        }).RequireRateLimiting("auth-user");

        group.MapGet("/items", async (
            HttpContext ctx,
            IMemoryRepository memoryRepository,
            string status = "active",
            string? type = null) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            if (!MemoryStatusHelper.IsValid(status))
            {
                throw new InvalidInputException("不支持的记忆状态。");
            }

            if (!string.IsNullOrWhiteSpace(type) && !MemoryTypeHelper.IsValid(type))
            {
                throw new InvalidInputException("不支持的记忆类型。");
            }

            var memories = await memoryRepository.ListByUserAsync(userId, type, status);
            var data = memories
                .Where(memory => !IsExpiredMemory(memory))
                .OrderByDescending(memory => memory.UpdatedAt ?? memory.CreatedAt)
                .ThenBy(memory => memory.Id, StringComparer.Ordinal)
                .Select(MemoryItemDto.FromMemory)
                .ToArray();

            return Results.Ok(new MemoryItemsResponse
            {
                Success = true,
                Data = data
            });
        }).RequireRateLimiting("auth-user");

        group.MapPost("/items/{memoryId}/archive", async (
            string memoryId,
            HttpContext ctx,
            IMemoryRepository memoryRepository) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            if (string.IsNullOrWhiteSpace(memoryId))
            {
                throw new InvalidInputException("记忆 id 不能为空。");
            }

            var existing = await memoryRepository.GetAsync(userId, memoryId);
            if (existing == null)
            {
                throw new InvalidInputException("这条记忆不存在或不属于当前用户。");
            }

            var memory = await memoryRepository.ArchiveAsync(userId, memoryId);
            return Results.Ok(new MemoryItemResponse
            {
                Success = true,
                Data = MemoryItemDto.FromMemory(memory)
            });
        }).RequireRateLimiting("auth-user");

        group.MapPost("/review-inbox/{candidateId}/keep", async (
            string candidateId,
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
            IMemoryReviewStateStore memoryReviewStateStore) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var candidate = await FindReviewCandidateAsync(
                userId,
                candidateId,
                lifeEventService,
                memoryReviewInboxPreviewService,
                memoryReviewStateStore);
            var state = await memoryReviewStateStore.UpsertAsync(
                userId,
                new MemoryReviewStateUpsertRequest(candidate, "kept", candidate.MemoryId));
            var updated = MemoryReviewInboxStateProjection.ApplyState(
                candidate,
                new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    [candidate.Id] = state
                });

            return Results.Ok(new MemoryReviewCandidateActionResponse
            {
                Success = true,
                Data = updated
            });
        }).RequireRateLimiting("auth-user");

        group.MapPost("/review-inbox/{candidateId}/dismiss", async (
            string candidateId,
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
            IMemoryReviewStateStore memoryReviewStateStore) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var candidate = await FindReviewCandidateAsync(
                userId,
                candidateId,
                lifeEventService,
                memoryReviewInboxPreviewService,
                memoryReviewStateStore);
            var state = await memoryReviewStateStore.UpsertAsync(
                userId,
                new MemoryReviewStateUpsertRequest(candidate, "dismissed", candidate.MemoryId));
            var updated = MemoryReviewInboxStateProjection.ApplyState(
                candidate,
                new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    [candidate.Id] = state
                });

            return Results.Ok(new MemoryReviewCandidateActionResponse
            {
                Success = true,
                Data = updated
            });
        }).RequireRateLimiting("auth-user");

        group.MapPost("/review-inbox/{candidateId}/remember", async (
            string candidateId,
            MemoryReviewRememberRequest request,
            HttpContext ctx,
            IMemoryReviewRememberService memoryReviewRememberService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var result = await memoryReviewRememberService.RememberAsync(userId, candidateId, request);
            return Results.Ok(result);
        }).RequireRateLimiting("auth-user");

        group.MapGet("/context/preview", async (
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IMemoryContextPreviewService memoryContextPreviewService,
            int limit = 20) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var boundedLimit = Math.Clamp(limit, 1, 50);
            var events = await lifeEventService.ListEventsAsync(
                userId,
                type: "all",
                limit: boundedLimit,
                cursor: null,
                tag: null);

            var preview = memoryContextPreviewService.BuildPreview(userId, events.Data);

            return Results.Ok(new MemoryContextPreviewResponse
            {
                Success = true,
                Data = preview
            });
        }).RequireRateLimiting("auth-user");
    }

    private static async Task<MemoryReviewInboxPreviewData> BuildReviewPreviewAsync(
        string userId,
        int limit,
        ILifeEventService lifeEventService,
        IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
        IMemoryReviewStateStore memoryReviewStateStore)
    {
        var boundedLimit = Math.Clamp(limit, 1, 50);
        var events = await lifeEventService.ListEventsAsync(
            userId,
            type: "all",
            limit: boundedLimit,
            cursor: null,
            tag: null);

        var preview = memoryReviewInboxPreviewService.BuildPreview(userId, events.Data);
        var states = await memoryReviewStateStore.ListByCandidateIdsAsync(
            userId,
            preview.Candidates.Select(candidate => candidate.Id).ToArray());
        var keptCandidates = await memoryReviewStateStore.ListKeptCandidatesAsync(userId);

        var projected = MemoryReviewInboxStateProjection.Apply(preview, states);
        return MemoryReviewInboxStateProjection.AddMissingKeptCandidates(projected, keptCandidates);
    }

    private static async Task<MemoryReviewCandidateItem> FindReviewCandidateAsync(
        string userId,
        string candidateId,
        ILifeEventService lifeEventService,
        IMemoryReviewInboxPreviewService memoryReviewInboxPreviewService,
        IMemoryReviewStateStore memoryReviewStateStore)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new InvalidInputException("记忆候选 id 不能为空。");
        }

        var storedCandidate = await memoryReviewStateStore.GetCandidateAsync(userId, candidateId);
        if (storedCandidate != null)
        {
            return storedCandidate;
        }

        var events = await lifeEventService.ListEventsAsync(
            userId,
            type: "all",
            limit: 50,
            cursor: null,
            tag: null);
        var preview = memoryReviewInboxPreviewService.BuildPreview(userId, events.Data);
        var candidate = preview.Candidates.FirstOrDefault(item =>
            string.Equals(item.Id, candidateId, StringComparison.OrdinalIgnoreCase));

        if (candidate == null)
        {
            throw new InvalidInputException("这条记忆线索已经不在当前候选列表中。");
        }

        return candidate;
    }

    private static bool IsExpiredMemory(Memory memory)
    {
        return string.Equals(memory.Type, MemoryType.TemporaryContext.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase) &&
               memory.ExpiresAt.HasValue &&
               memory.ExpiresAt.Value <= DateTime.UtcNow;
    }
}
