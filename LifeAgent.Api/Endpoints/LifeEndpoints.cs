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

        // GET /api/life/events
        group.MapGet("/events", async (
            HttpContext ctx,
            ILifeEventService lifeEventService,
            string? type = "all",
            int limit = 20,
            string? cursor = null) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            // 调用 Service 执行带 Cursor 的翻页查询
            var result = await lifeEventService.ListEventsAsync(userId, type, limit, cursor);

            // 映射为 DTO，过滤掉 rawLlmOutput
            var dtoList = result.Data.Select(e => new TimelineEventDto
            {
                Id = e.Id,
                Type = e.Type,
                SchemaVersion = e.SchemaVersion,
                Title = e.Title,
                Content = e.Content,
                OccurredAt = e.OccurredAt.ToString("O"),
                CreatedAt = e.CreatedAt.ToString("O"),
                TimeZone = e.TimeZone,
                Tags = e.Tags,
                Importance = e.Importance,
                Source = e.Source,
                StructuredData = e.StructuredData,
                ExtractionConfidence = e.ExtractionConfidence,
                NeedsReview = e.NeedsReview
            }).ToList();

            return Results.Ok(new ListEventsResponse
            {
                Success = true,
                NextCursor = result.NextCursor,
                Data = dtoList
            });
        });

        // GET /api/life/events/{id}
        group.MapGet("/events/{id}", async (
            string id,
            HttpContext ctx,
            ILifeEventService lifeEventService,
            IWebHostEnvironment env) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var lifeEvent = await lifeEventService.GetEventAsync(userId, id);
            if (lifeEvent == null)
            {
                return Results.NotFound(new ErrorResponse
                {
                    Success = false,
                    Error = new ErrorDetail { Code = "EVENT_NOT_FOUND", Message = $"事件 {id} 不存在" }
                });
            }

            var dto = new EventDetailDto
            {
                Id = lifeEvent.Id,
                Type = lifeEvent.Type,
                SchemaVersion = lifeEvent.SchemaVersion,
                Title = lifeEvent.Title,
                Content = lifeEvent.Content,
                OccurredAt = lifeEvent.OccurredAt.ToString("O"),
                CreatedAt = lifeEvent.CreatedAt.ToString("O"),
                TimeZone = lifeEvent.TimeZone,
                Tags = lifeEvent.Tags,
                Importance = lifeEvent.Importance,
                Source = lifeEvent.Source,
                StructuredData = lifeEvent.StructuredData,
                ExtractionConfidence = lifeEvent.ExtractionConfidence,
                NeedsReview = lifeEvent.NeedsReview
            };

            // Debug / Development 环境下暴露 rawLlmOutput
            if (env.IsDevelopment() || env.EnvironmentName == "Debug")
            {
                dto.RawLlmOutput = lifeEvent.RawLlmOutput;
            }

            return Results.Ok(new EventDetailResponse
            {
                Success = true,
                Data = dto
            });
        });
    }
}
