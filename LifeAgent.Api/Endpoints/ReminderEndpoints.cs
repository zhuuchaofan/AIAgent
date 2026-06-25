using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Endpoints;

public static class ReminderEndpoints
{
    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reminders");

        // GET /api/reminders
        group.MapGet("", async (
            HttpContext ctx,
            IReminderService reminderService,
            string? status = "pending") =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var finalStatus = string.IsNullOrWhiteSpace(status) ? "pending" : status;
            var reminders = await reminderService.ListRemindersAsync(userId, finalStatus);

            var now = DateTime.UtcNow;
            var dtoList = reminders.Select(r =>
            {
                string displayStatus = r.Status;
                if (r.Status == "pending" && r.DueAt < now)
                {
                    displayStatus = "overdue";
                }

                return new ReminderDto
                {
                    Id = r.Id,
                    SourceEventId = r.SourceEventId,
                    Title = r.Title,
                    Description = r.Description,
                    DueAt = r.DueAt.ToString("O"),
                    Timezone = r.Timezone,
                    Status = r.Status,
                    DisplayStatus = displayStatus,
                    RepeatRule = r.RepeatRule,
                    CreatedAt = r.CreatedAt.ToString("O"),
                    UpdatedAt = r.UpdatedAt.ToString("O")
                };
            }).ToList();

            return Results.Ok(new
            {
                Success = true,
                Data = dtoList
            });
        });

        // PATCH /api/reminders/{id}
        group.MapPatch("/{id}", async (
            string id,
            UpdateReminderRequest request,
            HttpContext ctx,
            IReminderService reminderService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            if (request.Status == null && request.DueAt == null)
            {
                throw new InvalidInputException("status 和 dueAt 不能同时为空");
            }

            var success = await reminderService.UpdateReminderAsync(userId, id, request.Status, request.DueAt);
            if (!success)
            {
                throw new ReminderNotFoundException(id);
            }

            return Results.Ok(new
            {
                Success = true,
                Message = "提醒更新成功"
            });
        });
    }
}

public class ReminderDto
{
    public string Id { get; set; } = string.Empty;
    public string SourceEventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DueAt { get; set; } = string.Empty;   // ISO 8601 UTC
    public string Timezone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public string RepeatRule { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;  // ISO 8601 UTC
    public string UpdatedAt { get; set; } = string.Empty;  // ISO 8601 UTC
}

public class UpdateReminderRequest
{
    public string? Status { get; set; }
    public DateTime? DueAt { get; set; }
}
