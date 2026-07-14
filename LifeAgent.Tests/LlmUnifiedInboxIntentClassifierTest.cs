using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Agent.Phase8;
using LifeAgent.Api.Services.Agent.UnifiedInbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class LlmUnifiedInboxIntentClassifierTest
{
    [Fact]
    public async Task ClassifyAsync_ExplicitActionTypeBypassesLlm()
    {
        var classifier = Create(new ThrowingAnswerGenerator());

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "今天骑车回来",
            Summary: "今天骑车回来，心率不高。",
            RequestedActionType: Phase80PendingActionRuntime.LifeRecordPreview,
            ClientTimeZone: "Asia/Shanghai"));

        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, result.ActionType);
        Assert.Equal("explicit_action_type", result.Classifier);
    }

    [Fact]
    public async Task ClassifyAsync_ValidJsonRoutesToReminderPreview()
    {
        var generator = new CapturingAnswerGenerator
        {
            Answer = """{"actionType":"reminder_preview","reason":"explicit reminder"}"""
        };
        var classifier = Create(generator);

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "明天上午处理材料",
            Summary: "明天上午处理材料",
            RequestedActionType: null,
            ClientTimeZone: "Asia/Shanghai"));

        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, result.ActionType);
        Assert.Equal("llm_json_intent_classifier", result.Classifier);
        Assert.Contains("只能返回一个 JSON object", generator.LastSystemInstruction);
        Assert.Contains("首页输入", generator.LastUserPrompt);
    }

    [Fact]
    public async Task ClassifyAsync_MarkdownJsonRoutesToPlanPreview()
    {
        var classifier = Create(new CapturingAnswerGenerator
        {
            Answer = """
            ```json
            {"actionType":"plan_preview","reason":"planning"}
            ```
            """
        });

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "周末去哪里玩一下",
            Summary: "想规划一下周末",
            RequestedActionType: null,
            ClientTimeZone: null));

        Assert.Equal(Phase80PersonalHomeIntentRouter.PlanIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.PlanPreview, result.ActionType);
        Assert.Equal("llm_json_intent_classifier", result.Classifier);
    }

    [Fact]
    public async Task ClassifyAsync_InvalidJsonFallsBackToRuleBasedRouter()
    {
        var classifier = Create(new CapturingAnswerGenerator
        {
            Answer = "这是一段非 JSON 回答"
        });

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "提醒我明天喝水",
            Summary: "提醒我明天喝水",
            RequestedActionType: null,
            ClientTimeZone: "Asia/Shanghai"));

        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, result.ActionType);
        Assert.Equal("llm_json_invalid_rule_fallback", result.Classifier);
    }

    [Fact]
    public async Task ClassifyAsync_UnsupportedActionTypeFallsBackToRuleBasedRouter()
    {
        var classifier = Create(new CapturingAnswerGenerator
        {
            Answer = """{"actionType":"delete_calendar_event","reason":"unsafe"}"""
        });

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "计划一下新疆行程",
            Summary: "计划一下新疆行程",
            RequestedActionType: null,
            ClientTimeZone: "Asia/Shanghai"));

        Assert.Equal(Phase80PersonalHomeIntentRouter.PlanIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.PlanPreview, result.ActionType);
        Assert.Equal("llm_json_invalid_rule_fallback", result.Classifier);
    }

    [Fact]
    public async Task ClassifyAsync_GeneratorFailureFallsBackToRuleBasedRouter()
    {
        var classifier = Create(new ThrowingAnswerGenerator());

        var result = await classifier.ClassifyAsync(new UnifiedInboxIntentClassifierRequest(
            Title: "今天剪头发，会员卡还剩钱",
            Summary: "今天剪头发，会员卡还剩钱",
            RequestedActionType: null,
            ClientTimeZone: "Asia/Shanghai"));

        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, result.Intent);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, result.ActionType);
        Assert.Equal("llm_failed_rule_fallback", result.Classifier);
    }

    private static LlmUnifiedInboxIntentClassifier Create(IRagAnswerGenerator generator)
    {
        return new LlmUnifiedInboxIntentClassifier(
            generator,
            NullLogger<LlmUnifiedInboxIntentClassifier>.Instance);
    }

    private sealed class CapturingAnswerGenerator : IRagAnswerGenerator
    {
        public string Answer { get; init; } = """{"actionType":"life_record_preview","reason":"default"}""";
        public string? LastSystemInstruction { get; private set; }
        public string? LastUserPrompt { get; private set; }

        public Task<string> GenerateAnswerAsync(string systemInstruction, string userPrompt, List<ChatMessage> history)
        {
            LastSystemInstruction = systemInstruction;
            LastUserPrompt = userPrompt;
            return Task.FromResult(Answer);
        }
    }

    private sealed class ThrowingAnswerGenerator : IRagAnswerGenerator
    {
        public Task<string> GenerateAnswerAsync(string systemInstruction, string userPrompt, List<ChatMessage> history)
        {
            throw new InvalidOperationException("generator unavailable");
        }
    }
}
