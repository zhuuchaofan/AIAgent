using System.Diagnostics;
using System.Runtime.CompilerServices;
using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

[assembly: InternalsVisibleTo("LifeAgent.Tests")]

namespace LifeAgent.Api.Services;

/// <summary>
/// 每日总结服务实现。
/// 职责：时区转换 → 幂等缓存检查 → 查询当天事件 → 空数据日/LLM 分支 → 双写 Firestore。
/// </summary>
public class DailySummaryService : IDailySummaryService
{
    private readonly FirestoreDb _db;
    private readonly ILlmService _llm;
    private readonly ILogger<DailySummaryService> _logger;

    // 是否保存完整 Prompt（由环境变量控制，默认 false）
    private static readonly bool SaveFullPrompt =
        string.Equals(Environment.GetEnvironmentVariable("SAVE_FULL_AGENT_PROMPT"), "true",
            StringComparison.OrdinalIgnoreCase);

    public DailySummaryService(FirestoreDb db, ILlmService llm, ILogger<DailySummaryService> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // GenerateSummaryAsync
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public async Task<(DailySummary Summary, bool Cached)> GenerateSummaryAsync(
        string userId, string targetDate, string timeZone, bool forceRegenerate)
    {
        _logger.LogInformation(
            "GenerateSummaryAsync 开始：userId={UserId}, date={Date}, tz={TZ}, force={Force}",
            userId, targetDate, timeZone, forceRegenerate);

        // ── Step 1：检查缓存 ──────────────────────────────────────
        var summaryDocRef = _db
            .Collection("users").Document(userId)
            .Collection("daily_summaries").Document(targetDate);

        if (!forceRegenerate)
        {
            var snapshot = await summaryDocRef.GetSnapshotAsync();
            if (snapshot.Exists)
            {
                var cached = snapshot.ConvertTo<DailySummary>();
                _logger.LogInformation("命中缓存，直接返回 daily_summaries/{Date}", targetDate);
                return (cached, true);
            }
        }

        // ── Step 2：计算 UTC 时间区间 ─────────────────────────────
        var (periodStart, periodEnd) = GetUtcPeriod(targetDate, timeZone);

        _logger.LogInformation(
            "查询时间区间: [{Start:O}, {End:O}]（UTC）",
            periodStart, periodEnd);

        // ── Step 3：查询当天 LifeEvents（服务端过滤，isDeleted==false）──
        var eventsQuery = _db
            .Collection("users").Document(userId)
            .Collection("life_events")
            .WhereEqualTo("isDeleted", false)
            .WhereGreaterThanOrEqualTo("occurredAt", Timestamp.FromDateTime(periodStart))
            .WhereLessThan("occurredAt", Timestamp.FromDateTime(periodEnd))
            .OrderBy("occurredAt");

        var eventsSnapshot = await eventsQuery.GetSnapshotAsync();
        var events = eventsSnapshot.Documents
            .Select(d => d.ConvertTo<LifeEvent>())
            .ToList();

        _logger.LogInformation("当天事件数量: {Count}", events.Count);

        // ── Step 4：AgentRun 预写（记录开始时间）─────────────────
        var runId = $"run_{Guid.NewGuid():N}";
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        DailySummary summary;
        string agentStatus = "success";
        string? agentError = null;

        try
        {
            // ── Step 5：空数据日分支（不调用 LLM）──────────────────
            if (events.Count == 0)
            {
                _logger.LogInformation("空数据日，跳过 LLM 调用");
                summary = BuildEmptyDaySummary(userId, targetDate, timeZone, periodStart, periodEnd, runId, forceRegenerate);
            }
            else
            {
                // ── Step 6：调用 LLM 生成总结 ───────────────────────
                var summarized = await _llm.SummarizeAsync(events, targetDate, timeZone);

                summary = new DailySummary
                {
                    Id              = targetDate,
                    UserId          = userId,
                    Date            = targetDate,
                    TimeZone        = timeZone,
                    PeriodStartUtc  = periodStart,
                    PeriodEndUtc    = periodEnd,
                    EventCount      = events.Count,
                    Summary         = summarized.Summary,
                    Highlights      = summarized.Highlights ?? new(),
                    MoodLabel       = summarized.MoodLabel,
                    MoodScore       = summarized.MoodScore,
                    Suggestions     = summarized.Suggestions ?? new(),
                    GeneratedBy     = "llm",
                    AgentRunId      = runId,
                    CreatedAt       = startedAt,
                    UpdatedAt       = startedAt,
                    ForceRegenerated = forceRegenerate
                };
            }
        }
        catch (Exception ex)
        {
            agentStatus = "failed";
            agentError = ex.Message;
            stopwatch.Stop();

            // 写 AgentRun（失败记录）
            await WriteAgentRunAsync(userId, runId, targetDate, "failed", events.Count,
                null, agentError, startedAt, stopwatch.ElapsedMilliseconds);

            throw;
        }

        stopwatch.Stop();

        // ── Step 7：写入 daily_summaries（幂等覆写）──────────────
        await summaryDocRef.SetAsync(summary);
        _logger.LogInformation("daily_summaries/{Date} 写入完成", targetDate);

        // ── Step 8：写入 agent_runs ───────────────────────────────
        string? promptSummary = events.Count > 0
            ? $"日期：{targetDate} | 事件数：{events.Count} | 首条：{events[0].Title}"
            : null;

        await WriteAgentRunAsync(userId, runId, targetDate, agentStatus, events.Count,
            promptSummary, agentError, startedAt, stopwatch.ElapsedMilliseconds);

        return (summary, false);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // GetSummaryByDateAsync
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public async Task<DailySummary?> GetSummaryByDateAsync(string userId, string date)
    {
        var docRef = _db
            .Collection("users").Document(userId)
            .Collection("daily_summaries").Document(date);

        var snapshot = await docRef.GetSnapshotAsync();
        if (!snapshot.Exists) return null;

        return snapshot.ConvertTo<DailySummary>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 私有辅助方法
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 将用户本地日期（YYYY-MM-DD）和 IANA 时区转换为 UTC 区间 [dayStart, dayEnd)。
    /// dayEnd 为下一天的 00:00:00 UTC（半开区间），确保覆盖完整本地自然日。
    /// </summary>
    internal static (DateTime Start, DateTime End) GetUtcPeriod(string date, string timeZone)
    {
        // 解析日期
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var localDate))
            throw new ArgumentException($"日期格式错误，应为 YYYY-MM-DD，实际为：{date}", nameof(date));

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch
        {
            // 若 IANA ID 无法解析，回退到 UTC
            tz = TimeZoneInfo.Utc;
        }

        // 用户本地当天 00:00:00
        var localStart = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        // 用户本地次日 00:00:00（即半开区间上界）
        var localEnd   = localStart.AddDays(1);

        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var utcEnd   = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);

        return (utcStart, utcEnd);
    }

    /// <summary>构建空数据日的 DailySummary（不调用 LLM）</summary>
    internal static DailySummary BuildEmptyDaySummary(
        string userId, string date, string timeZone,
        DateTime periodStart, DateTime periodEnd,
        string runId, bool forceRegenerate)
    {
        var now = DateTime.UtcNow;
        return new DailySummary
        {
            Id              = date,
            UserId          = userId,
            Date            = date,
            TimeZone        = timeZone,
            PeriodStartUtc  = periodStart,
            PeriodEndUtc    = periodEnd,
            EventCount      = 0,
            Summary         = "这一天还没有记录。",
            Highlights      = new(),
            MoodLabel       = "暂无记录",
            MoodScore       = null,
            Suggestions     = new(),
            GeneratedBy     = "empty_day",
            AgentRunId      = runId,
            CreatedAt       = now,
            UpdatedAt       = now,
            ForceRegenerated = forceRegenerate
        };
    }

    /// <summary>写入 AgentRun 执行日志</summary>
    private async Task WriteAgentRunAsync(
        string userId, string runId, string targetDate, string status,
        int eventCount, string? promptSummary, string? errorMessage,
        DateTime startedAt, long durationMs)
    {
        var agentRun = new AgentRun
        {
            Id             = runId,
            UserId         = userId,
            TaskType       = "daily_summary",
            TargetDate     = targetDate,
            Status         = status,
            InputEventCount = eventCount,
            PromptSummary  = SaveFullPrompt ? promptSummary : (promptSummary?[..Math.Min(200, promptSummary?.Length ?? 0)]),
            FullPromptSaved = SaveFullPrompt,
            ErrorMessage   = errorMessage,
            StartedAt      = startedAt,
            CompletedAt    = DateTime.UtcNow,
            DurationMs     = durationMs
        };

        var runDocRef = _db
            .Collection("users").Document(userId)
            .Collection("agent_runs").Document(runId);

        await runDocRef.SetAsync(agentRun);
        _logger.LogInformation("agent_runs/{RunId} 写入完成（{Status}，耗时 {Ms}ms）", runId, status, durationMs);
    }
}
