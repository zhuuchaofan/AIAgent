using System.Text.Json;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public sealed class AgentActionExecutor
{
    private readonly ToolExecutor _toolExecutor;
    private readonly IPendingAgentActionStore _pendingActions;

    public AgentActionExecutor(ToolExecutor toolExecutor, IPendingAgentActionStore pendingActions)
    {
        _toolExecutor = toolExecutor;
        _pendingActions = pendingActions;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        string userId,
        AgentContext context,
        AgentRunRequest request,
        AgentExecutionContract contract,
        IReadOnlyList<PlannedToolCall> plan,
        CancellationToken cancellationToken)
    {
        if (contract.RequiresConfirmation)
        {
            var proposedAction = await CreateProposedActionAsync(
                userId,
                request.Message ?? string.Empty,
                contract,
                cancellationToken);

            return AgentExecutionResult.Confirmation(proposedAction);
        }

        if (plan.Count == 0)
        {
            return AgentExecutionResult.Fallback(new
            {
                intent = contract.Intent,
                fallback = AgentActionTypes.ReadonlyRag,
                reason = contract.FallbackReason ?? "no_plan"
            });
        }

        var toolCalls = new List<AgentToolCallResult>();
        foreach (var step in plan.Take(context.MaxIterations))
        {
            var toolCall = await _toolExecutor.ExecuteAsync(
                context,
                step.ToolName,
                step.Input,
                toolCalls.Count + 1,
                cancellationToken);
            toolCalls.Add(toolCall);

            if (toolCall.Status != "success")
            {
                break;
            }
        }

        return AgentExecutionResult.Tools(toolCalls, new
        {
            intent = contract.Intent,
            toolPlan = plan.Select(step => step.ToolName).ToArray()
        });
    }

    private async Task<AgentProposedAction> CreateProposedActionAsync(
        string userId,
        string message,
        AgentExecutionContract contract,
        CancellationToken cancellationToken)
    {
        var proposedActionType = contract.Intent switch
        {
            AgentIntentNames.Reminder => AgentActionTypes.CreateReminderPreview,
            AgentIntentNames.LifeEvent => AgentActionTypes.CreateLifeEvent,
            AgentIntentNames.Memory => AgentActionTypes.SaveMemoryPreview,
            _ => throw new InvalidOperationException($"Unsupported confirmation intent: {contract.Intent}")
        };
        var title = proposedActionType switch
        {
            AgentActionTypes.CreateReminderPreview => BuildReminderPreviewTitle(message),
            AgentActionTypes.CreateLifeEvent => BuildLifeEventPreviewTitle(message),
            _ => "保存一条记忆"
        };
        var payload = proposedActionType == AgentActionTypes.CreateLifeEvent
            ? BuildLifeEventPreviewPayload(message)
            : new
            {
                originalMessage = message,
                previewOnly = true
            };

        var pending = await _pendingActions.CreateAsync(
            userId,
            proposedActionType,
            title,
            "Agent 建议创建一条写入动作，但当前阶段仅支持确认流程预览，不会真正写入数据。",
            payload,
            "medium",
            TimeSpan.FromMinutes(10),
            cancellationToken);

        return pending.ProposedAction;
    }

    private static string BuildReminderPreviewTitle(string message)
    {
        return message.Contains("黑猫", StringComparison.OrdinalIgnoreCase)
            ? "明天观察黑猫状态"
            : "创建一条提醒预览";
    }

    private static string BuildLifeEventPreviewTitle(string message)
    {
        return message.Contains("黑猫", StringComparison.OrdinalIgnoreCase)
            ? "黑猫呕吐观察"
            : "记录一条生活事件";
    }

    private static object BuildLifeEventPreviewPayload(string message)
    {
        var containsCat = message.Contains("黑猫", StringComparison.OrdinalIgnoreCase) ||
                          message.Contains("猫", StringComparison.OrdinalIgnoreCase);
        var containsVomit = message.Contains("吐", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("呕吐", StringComparison.OrdinalIgnoreCase);
        var type = containsCat || containsVomit ? "pet_health" : "life_event";
        var title = BuildLifeEventPreviewTitle(message);
        var content = containsCat && containsVomit
            ? "今天黑猫吐了一次，暂时观察精神和食欲。"
            : "Agent 根据用户确认请求生成一条生活事件预览。";
        var tags = containsCat
            ? new[] { "猫", "健康" }
            : new[] { "生活事件" };

        return new
        {
            type,
            title,
            content,
            structuredData = new
            {
                tags,
                catName = containsCat ? "黑猫" : null,
                importance = containsVomit ? 2 : 1,
                rawExtractedHints = "agent_preview_life_event"
            }
        };
    }
}

public sealed record AgentExecutionResult(
    AgentProposedAction? ProposedAction,
    List<AgentToolCallResult> ToolCalls,
    object Payload)
{
    public static AgentExecutionResult Confirmation(AgentProposedAction proposedAction)
    {
        return new AgentExecutionResult(proposedAction, new List<AgentToolCallResult>(), proposedAction.Payload);
    }

    public static AgentExecutionResult Fallback(object payload)
    {
        return new AgentExecutionResult(null, new List<AgentToolCallResult>(), payload);
    }

    public static AgentExecutionResult Tools(List<AgentToolCallResult> toolCalls, object payload)
    {
        return new AgentExecutionResult(null, toolCalls, payload);
    }
}
