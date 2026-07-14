using System.Text.Json;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Agent.UnifiedInbox;

public sealed class LlmUnifiedInboxIntentClassifier : IUnifiedInboxIntentClassifier
{
    private const string DefaultTimeZone = "Asia/Shanghai";
    private const string LifeRecordActionType = Phase80PendingActionRuntime.LifeRecordPreview;
    private const string ReminderActionType = Phase80PendingActionRuntime.ReminderPreview;
    private const string PlanActionType = Phase80PendingActionRuntime.PlanPreview;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRagAnswerGenerator _answerGenerator;
    private readonly ILogger<LlmUnifiedInboxIntentClassifier> _logger;

    public LlmUnifiedInboxIntentClassifier(
        IRagAnswerGenerator answerGenerator,
        ILogger<LlmUnifiedInboxIntentClassifier> logger)
    {
        _answerGenerator = answerGenerator;
        _logger = logger;
    }

    public async Task<Phase80PersonalHomeIntentRoute> ClassifyAsync(
        UnifiedInboxIntentClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedActionType))
        {
            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                request.RequestedActionType,
                "explicit_action_type");
        }

        var source = $"{request.Title}\n{request.Summary}";
        try
        {
            var raw = await _answerGenerator.GenerateAnswerAsync(
                BuildSystemInstruction(),
                BuildUserPrompt(source, request.ClientTimeZone),
                new List<ChatMessage>());

            var actionType = TryParseActionType(raw);

            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                actionType,
                string.IsNullOrWhiteSpace(actionType)
                    ? "llm_json_invalid_rule_fallback"
                    : "llm_json_intent_classifier");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unified inbox LLM intent classification failed; falling back to rule-based classifier.");
            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                request.RequestedActionType,
                "llm_failed_rule_fallback");
        }
    }

    private static string BuildSystemInstruction()
    {
        return """
        你是 LifeOS 首页统一输入框的意图分类器，只判断用户这句话应该进入哪个安全预览动作。

        只能返回一个 JSON object，不要 Markdown，不要解释。

        可选 actionType:
        - life_record_preview: 用户在记录已经发生的生活、感受、状态、消费、运动、工作、见闻。
        - reminder_preview: 用户明确要求之后提醒、到点提醒、别忘了、闹钟、定时处理。
        - plan_preview: 用户在表达计划、安排、规划、准备做某事，但没有要求系统到点提醒。

        默认优先 life_record_preview。不要输出任何执行、外部工具或真实写入动作。

        JSON 形状:
        {"actionType":"life_record_preview","reason":"short_reason"}
        """;
    }

    private static string BuildUserPrompt(string source, string? clientTimeZone)
    {
        var timeZone = string.IsNullOrWhiteSpace(clientTimeZone) ? DefaultTimeZone : clientTimeZone.Trim();
        return $"""
        用户时区: {timeZone}

        首页输入:
        {source.Trim()}
        """;
    }

    private static string? TryParseActionType(string raw)
    {
        var json = LlmHelper.ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<UnifiedInboxIntentClassifierResponse>(json, JsonOptions);
            return NormalizeAllowedActionType(parsed?.ActionType);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeAllowedActionType(string? actionType)
    {
        return actionType?.Trim().ToLowerInvariant() switch
        {
            LifeRecordActionType => LifeRecordActionType,
            ReminderActionType => ReminderActionType,
            PlanActionType => PlanActionType,
            _ => null
        };
    }

    private sealed record UnifiedInboxIntentClassifierResponse(string? ActionType, string? Reason);
}
