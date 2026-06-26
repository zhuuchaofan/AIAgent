using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Endpoints;

public static class DailySummaryEndpoints
{
    public static void MapDailySummaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/daily-summaries");

        // ─────────────────────────────────────────────────────────────
        // POST /api/daily-summaries/generate
        // 生成（或返回缓存的）指定日期每日总结
        // ─────────────────────────────────────────────────────────────
        group.MapPost("/generate", async (
            GenerateSummaryRequest request,
            HttpContext ctx,
            IDailySummaryService summaryService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
                throw new UnauthorizedException();

            // 验证 targetDate 格式
            if (string.IsNullOrWhiteSpace(request.TargetDate) ||
                !DateOnly.TryParseExact(request.TargetDate, "yyyy-MM-dd", out _))
            {
                throw new InvalidInputException("targetDate 格式错误，必须为 YYYY-MM-DD（如：2026-06-26）");
            }

            var timeZone = string.IsNullOrWhiteSpace(request.ClientTimeZone)
                ? "UTC"
                : request.ClientTimeZone;

            var (summary, cached) = await summaryService.GenerateSummaryAsync(
                userId,
                request.TargetDate,
                timeZone,
                request.ForceRegenerate);

            return Results.Json(new
            {
                success = true,
                cached  = cached,
                data    = MapToDto(summary)
            }, statusCode: 201);
        });

        // ─────────────────────────────────────────────────────────────
        // GET /api/daily-summaries/{date}
        // 查询已生成的某天总结（不生成新的）
        // ─────────────────────────────────────────────────────────────
        group.MapGet("/{date}", async (
            string date,
            HttpContext ctx,
            IDailySummaryService summaryService) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
                throw new UnauthorizedException();

            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out _))
                throw new InvalidInputException("date 格式错误，必须为 YYYY-MM-DD");

            var summary = await summaryService.GetSummaryByDateAsync(userId, date);
            if (summary == null)
                return Results.NotFound(new { success = false, error = new { message = $"未找到 {date} 的每日总结" } });

            return Results.Ok(new { success = true, data = MapToDto(summary) });
        });
    }

    /// <summary>将 DailySummary 实体转为 API 响应 DTO（避免暴露内部字段）</summary>
    private static DailySummaryDto MapToDto(DailySummary s) => new()
    {
        Id               = s.Id,
        Date             = s.Date,
        TimeZone         = s.TimeZone,
        EventCount       = s.EventCount,
        Summary          = s.Summary,
        Highlights       = s.Highlights,
        MoodLabel        = s.MoodLabel,
        MoodScore        = s.MoodScore,
        Suggestions      = s.Suggestions,
        GeneratedBy      = s.GeneratedBy,
        AgentRunId       = s.AgentRunId,
        CreatedAt        = s.CreatedAt.ToString("O"),
        UpdatedAt        = s.UpdatedAt.ToString("O"),
        ForceRegenerated = s.ForceRegenerated
    };
}

/// <summary>POST /api/daily-summaries/generate 请求体</summary>
public class GenerateSummaryRequest
{
    /// <summary>目标日期，格式 YYYY-MM-DD（用户本地时区）</summary>
    public string TargetDate { get; set; } = string.Empty;

    /// <summary>用户本地时区（IANA），如 "Asia/Shanghai"</summary>
    public string ClientTimeZone { get; set; } = "UTC";

    /// <summary>是否强制重新生成，忽略缓存（默认 false）</summary>
    public bool ForceRegenerate { get; set; } = false;
}

/// <summary>每日总结 API 响应 DTO</summary>
public class DailySummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
    public string MoodLabel { get; set; } = string.Empty;
    public double? MoodScore { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public string GeneratedBy { get; set; } = string.Empty;
    public string AgentRunId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool ForceRegenerated { get; set; }
}
