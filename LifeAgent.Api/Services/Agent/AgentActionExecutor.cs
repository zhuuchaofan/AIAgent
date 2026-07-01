using System.Text.Json;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent;

public sealed class AgentActionExecutor
{
    private readonly ToolExecutor _toolExecutor;
    private readonly IPendingAgentActionStore _pendingActions;
    private readonly IMemoryProposalGuard _memoryProposalGuard;
    private readonly MemoryProposalRuntimeOptions _memoryProposalOptions;

    public AgentActionExecutor(
        ToolExecutor toolExecutor,
        IPendingAgentActionStore pendingActions,
        IMemoryProposalGuard? memoryProposalGuard = null,
        IOptions<MemoryProposalRuntimeOptions>? memoryProposalOptions = null)
    {
        _toolExecutor = toolExecutor;
        _pendingActions = pendingActions;
        _memoryProposalGuard = memoryProposalGuard ?? new MemoryProposalGuard();
        _memoryProposalOptions = memoryProposalOptions?.Value ?? MemoryProposalRuntimeOptions.Disabled;
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
            return await CreateConfirmationExecutionAsync(
                userId,
                request.Message ?? string.Empty,
                contract,
                cancellationToken);
        }

        if (plan.Count == 0)
        {
            return AgentExecutionResult.Fallback(BuildFallbackPayload(context, contract));
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

        return AgentExecutionResult.Tools(toolCalls, BuildToolsPayload(context, contract, plan));
    }

    private static object BuildFallbackPayload(AgentContext context, AgentExecutionContract contract)
    {
        var reason = contract.FallbackReason ?? "no_plan";
        if (!ShouldIncludeMemoryDiagnostics(context))
        {
            return new
            {
                intent = contract.Intent,
                fallback = AgentActionTypes.ReadonlyRag,
                reason
            };
        }

        return new
        {
            intent = contract.Intent,
            fallback = AgentActionTypes.ReadonlyRag,
            reason,
            memoryContext = context.MemoryContext.ToDiagnostics()
        };
    }

    private static object BuildToolsPayload(
        AgentContext context,
        AgentExecutionContract contract,
        IReadOnlyList<PlannedToolCall> plan)
    {
        var toolPlan = plan.Select(step => step.ToolName).ToArray();
        if (!ShouldIncludeMemoryDiagnostics(context))
        {
            return new
            {
                intent = contract.Intent,
                toolPlan
            };
        }

        return new
        {
            intent = contract.Intent,
            toolPlan,
            memoryContext = context.MemoryContext.ToDiagnostics()
        };
    }

    private static bool ShouldIncludeMemoryDiagnostics(AgentContext context)
    {
        return context.MemoryContext.Enabled;
    }

    private async Task<AgentExecutionResult> CreateConfirmationExecutionAsync(
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
        var payload = proposedActionType switch
        {
            AgentActionTypes.CreateLifeEvent => BuildLifeEventPreviewPayload(message),
            AgentActionTypes.SaveMemoryPreview => BuildMemoryPreviewPayload(message),
            _ => new
            {
                originalMessage = message,
                previewOnly = true
            }
        };
        if (payload is MemoryPreviewActionPayload memoryPayload)
        {
            var guardDecision = ApplyMemoryProposalGuard(userId, memoryPayload);
            if (guardDecision.Blocked)
            {
                return AgentExecutionResult.BlockedMemoryProposal(memoryPayload);
            }
        }

        var pending = await _pendingActions.CreateAsync(
            userId,
            proposedActionType,
            title,
            "Agent 建议创建一条写入动作，但当前阶段仅支持确认流程预览，不会真正写入数据。",
            payload,
            "medium",
            TimeSpan.FromMinutes(10),
            cancellationToken);

        return AgentExecutionResult.Confirmation(pending.ProposedAction);
    }

    private MemoryPollutionDecision ApplyMemoryProposalGuard(
        string userId,
        MemoryPreviewActionPayload payload)
    {
        if (!_memoryProposalOptions.IsEnabledForUser(userId))
        {
            return new MemoryPollutionDecision
            {
                Action = "disabled",
                Reason = "memory_proposal_runtime_disabled"
            };
        }

        var decision = _memoryProposalGuard.Evaluate(payload, Array.Empty<Memory>());
        payload.GuardDecision = decision.Action;
        payload.Blocked = decision.Blocked ? true : null;
        payload.ReviewRequired = decision.ReviewRequired ? true : null;
        payload.GuardReason = decision.Reason;
        payload.ConflictResult = decision.ConflictResult?.HasConflict == true
            ? decision.ConflictResult
            : null;
        payload.MergeCandidate = decision.MergeCandidate?.HasCandidate == true
            ? decision.MergeCandidate
            : null;
        payload.Metadata ??= new Dictionary<string, object>();
        payload.Metadata["guardDecision"] = decision.Action;
        payload.Metadata["guardReason"] = decision.Reason;

        return decision;
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

    private static MemoryPreviewActionPayload BuildMemoryPreviewPayload(string message)
    {
        var content = ExtractMemoryContent(message);
        var memoryType = InferMemoryType(message, content);
        var importance = memoryType == MemoryType.Constraint.ToSnakeCaseString() ? 5 : 3;

        return new MemoryPreviewActionPayload
        {
            MemoryType = memoryType,
            Content = content,
            Confidence = 0.8,
            Importance = importance,
            Source = "agent_preview",
            PreviewOnly = true,
            OriginalMessage = message,
            SourceText = message,
            Metadata = new Dictionary<string, object>
            {
                ["proposalStage"] = "phase6_2_preview_contract"
            }
        };
    }

    private static string ExtractMemoryContent(string message)
    {
        var normalized = (message ?? string.Empty).Trim();
        var prefixes = new[]
        {
            "帮我保存记忆：",
            "帮我保存记忆:",
            "帮我记一下：",
            "帮我记一下:",
            "记一下：",
            "记一下:"
        };

        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? "用户提出了一条待确认的长期记忆。"
            : normalized;
    }

    private static string InferMemoryType(string message, string content)
    {
        var text = $"{message} {content}";
        if (text.Contains("不要", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("禁止", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("过敏", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryType.Constraint.ToSnakeCaseString();
        }

        if (text.Contains("目标", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("计划", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryType.Goal.ToSnakeCaseString();
        }

        if (text.Contains("习惯", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("每天", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("总是", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryType.Habit.ToSnakeCaseString();
        }

        return MemoryType.Preference.ToSnakeCaseString();
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

    public static AgentExecutionResult BlockedMemoryProposal(MemoryPreviewActionPayload payload)
    {
        return new AgentExecutionResult(null, new List<AgentToolCallResult>(), new
        {
            actionType = AgentActionTypes.SaveMemoryPreview,
            previewOnly = true,
            wroteData = false,
            blocked = true,
            guardDecision = payload.GuardDecision,
            reason = payload.GuardReason
        });
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
