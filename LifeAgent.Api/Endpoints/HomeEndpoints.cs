using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Services.Home;

namespace LifeAgent.Api.Endpoints;

public static class HomeEndpoints
{
    public static void MapHomeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/home");

        group.MapGet("/overview", async (
            HttpContext ctx,
            IHomeOverviewService homeOverviewService,
            int limit = 20,
            string? timeZone = null) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var data = await homeOverviewService.BuildAsync(userId, limit, timeZone);
            return Results.Ok(new HomeOverviewResponse
            {
                Success = true,
                Data = data
            });
        }).RequireRateLimiting("auth-user");
    }
}
