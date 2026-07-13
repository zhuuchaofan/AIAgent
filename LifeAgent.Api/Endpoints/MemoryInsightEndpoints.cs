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

            var preview = memoryReviewInboxPreviewService.BuildPreview(userId, events.Data);

            return Results.Ok(new MemoryReviewInboxPreviewResponse
            {
                Success = true,
                Data = preview
            });
        }).RequireRateLimiting("auth-user");
    }
}
