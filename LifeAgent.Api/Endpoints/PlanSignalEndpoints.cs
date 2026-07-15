using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Services.Plans;

namespace LifeAgent.Api.Endpoints;

public static class PlanSignalEndpoints
{
    public static void MapPlanSignalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plan-signals");

        group.MapGet("", async (
            HttpContext ctx,
            IPlanSignalService planSignalService,
            string? status = "active") =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var finalStatus = string.IsNullOrWhiteSpace(status) ? "active" : status;
            var signals = await planSignalService.ListAsync(userId, finalStatus);
            return Results.Ok(new
            {
                Success = true,
                Data = signals.Select(signal => new PlanSignalDto
                {
                    Id = signal.Id,
                    Kind = signal.Kind,
                    SourceActionId = signal.SourceActionId,
                    SourceActionType = signal.SourceActionType,
                    Title = signal.Title,
                    Content = signal.Content,
                    Status = signal.Status,
                    CreatedAt = signal.CreatedAt.ToString("O"),
                    UpdatedAt = signal.UpdatedAt.ToString("O"),
                    ArchivedAt = signal.ArchivedAt?.ToString("O")
                }).ToList()
            });
        }).RequireRateLimiting("auth-user");

        group.MapPatch("/{id}/archive", async (
            string id,
            HttpContext ctx,
            IPlanSignalService planSignalService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var success = await planSignalService.ArchiveAsync(userId, id);
            if (!success)
            {
                return Results.NotFound(new
                {
                    Success = false,
                    Message = "计划线索不存在"
                });
            }

            return Results.Ok(new
            {
                Success = true,
                Message = "计划线索已归档"
            });
        }).RequireRateLimiting("auth-user");
    }
}

public sealed class PlanSignalDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string SourceActionId { get; set; } = string.Empty;
    public string SourceActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? ArchivedAt { get; set; }
}
