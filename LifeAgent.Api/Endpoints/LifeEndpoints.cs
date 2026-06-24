using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Endpoints;

public static class LifeEndpoints
{
    public static void MapLifeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/life");

        // POST /api/life/ingest
        group.MapPost("/ingest", async (
            IngestRequest request,
            HttpContext ctx,
            ILifeEventService lifeEventService,
            ILlmService llmService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new ErrorResponse
                {
                    Success = false,
                    Error = new ErrorDetail { Code = "INVALID_INPUT", Message = "Text 不能为空" }
                });
            }

            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            // 优先使用请求体中的时区，缺失时暂时默认 Asia/Tokyo
            var timeZone = !string.IsNullOrWhiteSpace(request.ClientTimeZone) ? request.ClientTimeZone : "Asia/Tokyo";

            // 调用 Mock 解析器
            var parsedEvent = await llmService.ParseAsync(request.Text, timeZone);

            var lifeEvent = new LifeEvent
            {
                Type = parsedEvent.Type,
                Title = parsedEvent.Title,
                Content = request.Text,
                TimeZone = timeZone,
                Tags = parsedEvent.Tags,
                Importance = parsedEvent.Importance,
                StructuredData = parsedEvent.StructuredData,
                ExtractionConfidence = parsedEvent.ExtractionConfidence,
                NeedsReview = parsedEvent.NeedsReview,
                RawLlmOutput = parsedEvent.RawLlmOutput
            };

            // 保存到 Firestore
            var savedEvent = await lifeEventService.SaveEventAsync(userId, lifeEvent);

            string message = "已成功记录事件。";
            if (parsedEvent.DetectedReminderIntent)
            {
                message += "检测到提醒意图，但阶段 1 暂不支持提醒自动创建，该功能将在后续阶段开启。";
            }

            return Results.Ok(new IngestResponse
            {
                Success = true,
                Message = message,
                DetectedReminderIntent = parsedEvent.DetectedReminderIntent,
                ReminderCreated = false,
                Data = new IngestResponseData
                {
                    Id = savedEvent.Id,
                    Type = savedEvent.Type,
                    SchemaVersion = savedEvent.SchemaVersion,
                    Title = savedEvent.Title,
                    Content = savedEvent.Content,
                    OccurredAt = savedEvent.OccurredAt.ToString("O"),
                    CreatedAt = savedEvent.CreatedAt.ToString("O"),
                    TimeZone = savedEvent.TimeZone,
                    Tags = savedEvent.Tags,
                    Importance = savedEvent.Importance,
                    Source = savedEvent.Source,
                    StructuredData = savedEvent.StructuredData,
                    ExtractionConfidence = savedEvent.ExtractionConfidence,
                    NeedsReview = savedEvent.NeedsReview
                }
            });
        });
    }
}
