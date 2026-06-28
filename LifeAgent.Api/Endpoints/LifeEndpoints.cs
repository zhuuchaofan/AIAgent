using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models.Exceptions;
using static LifeAgent.Api.Services.DailyQuotaService;

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
            ILlmService llmService,
            IDailyQuotaService quotaService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                throw new InvalidInputException("Text 不能为空");
            }

            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            if (!quotaService.CheckAndIncrement(userId, QuotaTypeLlm))
            {
                throw new QuotaExceededException("AI 文本解析", quotaService.GetRemaining(userId, QuotaTypeLlm));
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

            // Schema 强类型约束校验与过滤
            LifeEventSchemaValidator.ValidateAndSanitize(lifeEvent);

            // 保存到 Firestore (原子双写)
            var (savedEvent, createdReminder) = await lifeEventService.SaveEventWithReminderAsync(userId, lifeEvent, parsedEvent);

            string message = "已成功记录事件。";
            bool reminderCreated = false;
            if (savedEvent.ReminderIntentDetected)
            {
                if (savedEvent.ReminderParseStatus == "success" && createdReminder != null)
                {
                    message += $"已自动创建关联的提醒事项(ID: {createdReminder.Id})。";
                    reminderCreated = true;
                }
                else if (savedEvent.ReminderParseStatus == "missing_due_time")
                {
                    message += "检测到提醒意图，但因完全缺失时间未能创建提醒事项。";
                }
                else if (savedEvent.ReminderParseStatus == "invalid_due_time")
                {
                    message += "检测到提醒意图，但因时间格式不合法未能创建提醒事项。";
                }
            }

            return Results.Ok(new IngestResponse
            {
                Success = true,
                Message = message,
                DetectedReminderIntent = savedEvent.ReminderIntentDetected,
                ReminderCreated = reminderCreated,
                ReminderId = createdReminder?.Id,
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
                    NeedsReview = savedEvent.NeedsReview,
                    ReminderId = createdReminder?.Id
                }
            });
        });

        // GET /api/life/events
        group.MapGet("/events", async (
            HttpContext ctx,
            ILifeEventService lifeEventService,
            string? type = "all",
            int limit = 20,
            string? cursor = null,
            string? tag = null) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            // 调用 Service 执行带 Cursor 的翻页查询
            var result = await lifeEventService.ListEventsAsync(userId, type, limit, cursor, tag);

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
                throw new UnauthorizedException();
            }

            var lifeEvent = await lifeEventService.GetEventAsync(userId, id);
            if (lifeEvent == null)
            {
                throw new EventNotFoundException(id);
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

        // PUT /api/life/events/{id}
        group.MapPut("/events/{id}", async (
            string id,
            UpdateEventRequest request,
            HttpContext ctx,
            ILifeEventService lifeEventService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            // 将 request.StructuredData 中的 JsonElement 递归转换为原始 .NET 类型防止 Firestore 报错
            var cleanedStructuredData = new Dictionary<string, object>();
            if (request.StructuredData != null)
            {
                foreach (var kvp in request.StructuredData)
                {
                    var converted = ConvertJsonElement(kvp.Value);
                    if (converted != null)
                    {
                        cleanedStructuredData[kvp.Key] = converted;
                    }
                }
            }

            var updatedEvent = new LifeEvent
            {
                Title = request.Title,
                Content = request.Content,
                Tags = request.Tags ?? new(),
                Importance = request.Importance,
                StructuredData = cleanedStructuredData,
                Type = request.Type ?? string.Empty
            };

            var success = await lifeEventService.UpdateEventAsync(userId, id, updatedEvent);
            if (!success)
            {
                throw new EventNotFoundException(id);
            }

            return Results.Ok(new { success = true, message = "更新成功" });
        });

        // DELETE /api/life/events/{id}
        group.MapDelete("/events/{id}", async (
            string id,
            HttpContext ctx,
            ILifeEventService lifeEventService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedException();
            }

            var success = await lifeEventService.SoftDeleteEventAsync(userId, id);
            if (!success)
            {
                throw new EventNotFoundException(id);
            }

            return Results.Ok(new { success = true, message = "事件已成功软删除" });
        });
    }

    private static object? ConvertJsonElement(object value)
    {
        if (value is System.Text.Json.JsonElement je)
        {
            switch (je.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return je.GetString();
                case System.Text.Json.JsonValueKind.Number:
                    if (je.TryGetInt64(out long l)) return l;
                    return je.GetDouble();
                case System.Text.Json.JsonValueKind.True:
                    return true;
                case System.Text.Json.JsonValueKind.False:
                    return false;
                case System.Text.Json.JsonValueKind.Null:
                    return null;
                case System.Text.Json.JsonValueKind.Object:
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in je.EnumerateObject())
                    {
                        dict[prop.Name] = ConvertJsonElement(prop.Value);
                    }
                    return dict;
                case System.Text.Json.JsonValueKind.Array:
                    var list = new List<object?>();
                    foreach (var item in je.EnumerateArray())
                    {
                        list.Add(ConvertJsonElement(item));
                    }
                    return list;
            }
        }
        return value;
    }
}
