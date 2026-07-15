using LifeAgent.Api.Models;
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
                Data = signals.Select(PlanSignalDto.From).ToList()
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

        group.MapPost("/{id}/convert-reminder", ConvertPlanSignalToReminderAsync)
            .RequireRateLimiting("auth-user");
    }

    internal static async Task<IResult> ConvertPlanSignalToReminderAsync(
        string id,
        HttpContext ctx,
        IPlanSignalService planSignalService,
        ConvertPlanSignalToReminderRequest request)
    {
        var userId = ctx.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedException();
        }

        var result = await planSignalService.ConvertReminderSignalAsync(
            userId,
            id,
            new PlanSignalReminderConversionRequest(
                request.DueAt,
                request.Timezone,
                request.Title,
                request.Description));

        if (result is null)
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
            Message = "已保存为提醒",
            Data = new ConvertPlanSignalToReminderResponse
            {
                Signal = PlanSignalDto.From(result.Signal),
                Reminder = PlanSignalReminderDto.From(result.Reminder)
            }
        });
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
    public string? ConvertedAt { get; set; }
    public string? ConvertedReminderId { get; set; }

    public static PlanSignalDto From(PlanSignal signal)
    {
        return new PlanSignalDto
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
            ArchivedAt = signal.ArchivedAt?.ToString("O"),
            ConvertedAt = signal.ConvertedAt?.ToString("O"),
            ConvertedReminderId = signal.ConvertedReminderId
        };
    }
}

public sealed class ConvertPlanSignalToReminderRequest
{
    public DateTime DueAt { get; set; }
    public string? Timezone { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public sealed class ConvertPlanSignalToReminderResponse
{
    public PlanSignalDto Signal { get; set; } = new();
    public PlanSignalReminderDto Reminder { get; set; } = new();
}

public sealed class PlanSignalReminderDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DueAt { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public static PlanSignalReminderDto From(Reminder reminder)
    {
        return new PlanSignalReminderDto
        {
            Id = reminder.Id,
            Title = reminder.Title,
            Description = reminder.Description,
            DueAt = reminder.DueAt.ToString("O"),
            Timezone = reminder.Timezone,
            Status = reminder.Status
        };
    }
}
