using System.Text.RegularExpressions;

namespace LifeAgent.Api.Services;

/// <summary>
/// Phase 1 Mock LLM 解析器，用关键词规则模拟大模型的结构化提取行为。
/// 后续可无缝替换为 GeminiLlmService，无需修改上层接口。
/// </summary>
public class MockLlmService : ILlmService
{
    private readonly ILogger<MockLlmService> _logger;

    public MockLlmService(ILogger<MockLlmService> logger)
    {
        _logger = logger;
    }

    public Task<ParsedEvent> ParseAsync(string text, string timeZone)
    {
        _logger.LogInformation("MockLlmService 开始解析文本（长度={Len}）", text.Length);

        var result = new ParsedEvent
        {
            // 记录 Mock 原始输出（调试用）
            RawLlmOutput = $"[MOCK] input={text}"
        };

        // ── 1. 提醒意图检测（优先于事件分类）──────────────────────────────
        // 关键词：提醒我、提醒一下、明天提醒、记得提醒
        result.DetectedReminderIntent =
            text.Contains("提醒我") ||
            text.Contains("提醒一下") ||
            text.Contains("明天提醒") ||
            text.Contains("记得提醒") ||
            Regex.IsMatch(text, @"提醒.{0,5}(一下|我|我一下)");

        // ── 2. 事件类型分类 & 字段提取 ─────────────────────────────────────
        if (IsCycling(text))
        {
            BuildCyclingEvent(text, result);
        }
        else if (IsCat(text))
        {
            BuildCatEvent(text, result);
        }
        else
        {
            BuildUnknownEvent(text, result);
        }

        _logger.LogInformation(
            "MockLlmService 解析完成：type={Type}, confidence={Conf}, reminderIntent={Reminder}",
            result.Type, result.ExtractionConfidence, result.DetectedReminderIntent);

        return Task.FromResult(result);
    }

    // ── 分类判断 ──────────────────────────────────────────────────────────

    private static bool IsCycling(string text) =>
        text.Contains("骑行") || text.Contains("骑车");

    private static bool IsCat(string text) =>
        text.Contains("猫");

    // ── 各类型事件构建 ────────────────────────────────────────────────────

    private static void BuildCyclingEvent(string text, ParsedEvent result)
    {
        result.Type = "cycling";
        result.Tags = ["骑行", "运动", "健康"];
        result.Importance = 3;
        result.ExtractionConfidence = 0.85;
        result.NeedsReview = false;

        // 提取距离：支持 "18km" "18公里" "18千米"
        var distanceMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(?:km|公里|千米)", RegexOptions.IgnoreCase);
        double? distanceKm = distanceMatch.Success
            ? double.Parse(distanceMatch.Groups[1].Value)
            : null;

        // 提取心率：支持 "心率145" "平均心率145"
        var hrMatch = Regex.Match(text, @"(?:心率|平均心率)\s*(\d+)");
        int? avgHeartRate = hrMatch.Success ? int.Parse(hrMatch.Groups[1].Value) : null;

        // 提取时长：支持 "骑了60分钟" "60min"
        var durMatch = Regex.Match(text, @"(\d+)\s*(?:分钟|min)", RegexOptions.IgnoreCase);
        int? durationMinutes = durMatch.Success ? int.Parse(durMatch.Groups[1].Value) : null;

        // 疲劳度检测
        string? fatigue = text.Contains("很累") || text.Contains("疲惫") || text.Contains("腿酸") || text.Contains("大腿酸") || text.Contains("酸")
            ? "medium"
            : null;

        // 构建标题
        result.Title = distanceKm.HasValue
            ? $"骑行 {distanceKm}km"
            : "骑行记录";

        // 按规则只写入有值的字段（严禁写 0 默认值）
        var sd = new Dictionary<string, object>();
        if (distanceKm.HasValue)    sd["distanceKm"]      = distanceKm.Value;
        if (avgHeartRate.HasValue)  sd["avgHeartRate"]     = avgHeartRate.Value;
        if (durationMinutes.HasValue) sd["durationMinutes"] = durationMinutes.Value;
        if (fatigue != null)         sd["fatigue"]          = fatigue;
        result.StructuredData = sd;
    }

    private static void BuildCatEvent(string text, ParsedEvent result)
    {
        result.Type = "cat";
        result.Tags = ["猫", "宠物"];
        result.Importance = 2;
        result.ExtractionConfidence = 0.80;
        result.NeedsReview = false;

        // 尝试提取猫咪名字：常见格式"小橘/大橘/猫咪叫XXX"
        var nameMatch = Regex.Match(text, @"(?:叫|名叫|是|的猫|猫咪)\s*([\u4e00-\u9fa5a-zA-Z]{1,6})");
        string? catName = nameMatch.Success ? nameMatch.Groups[1].Value : null;

        result.Title = catName != null ? $"与 {catName} 的互动" : "与猫咪的互动";

        var sd = new Dictionary<string, object>();
        if (catName != null) sd["catName"] = catName;
        result.StructuredData = sd;
    }

    private static void BuildUnknownEvent(string text, ParsedEvent result)
    {
        result.Type = "unknown";
        result.Tags = [];
        result.Importance = 2;
        result.ExtractionConfidence = 0.50;
        result.NeedsReview = true;           // 未识别类型，建议人工确认
        result.StructuredData = new();       // 空 Map，无任何默认 0 值

        // 取文本前 20 字作为标题
        result.Title = text.Length <= 20 ? text : text[..20] + "…";
    }
}
