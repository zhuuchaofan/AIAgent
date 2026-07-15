using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Plans;

public sealed class Phase80PlanSignalConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    public const string ExecutorId = "phase80_plan_signal_confirm_write_executor";
    private readonly IPlanSignalService _planSignalService;

    public Phase80PlanSignalConfirmWriteExecutor(IPlanSignalService planSignalService)
    {
        _planSignalService = planSignalService;
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        var ready = string.Equals(plan.Target, Phase80PendingActionRuntime.ConfirmTargetPlanSignals, StringComparison.Ordinal);
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: ready,
            RealPathReady: ready,
            ExecutorId: ExecutorId,
            Reason: ready
                ? "plan_signal_confirm_write_executor_ready"
                : "executor_only_supports_plan_signals");
    }

    public async Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Plan.Target, Phase80PendingActionRuntime.ConfirmTargetPlanSignals, StringComparison.Ordinal))
        {
            return Skipped(request, "unsupported_confirm_write_target");
        }

        var kind = string.Equals(request.ActionType, Phase80PendingActionRuntime.ReminderPreview, StringComparison.Ordinal)
            ? "reminder_signal"
            : "plan";
        var title = CleanTitle(request.Title, request.ActionType);
        var content = CleanContent(request.Summary, title);
        var signal = new PlanSignal
        {
            Id = $"plan_{Guid.NewGuid():N}",
            UserId = request.UserId,
            Kind = kind,
            SourceActionId = request.ActionId,
            SourceActionType = request.ActionType,
            Title = title,
            Content = content,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _planSignalService.CreateAsync(request.UserId, signal, cancellationToken);

        return new Phase80ConfirmWriteExecutionResult(
            Success: true,
            Status: "written",
            Target: request.Plan.Target,
            ResourcePath: $"users/{request.UserId}/plan_signals/{created.Id}",
            WroteData: true,
            RealWritePath: true,
            ExecutorId: ExecutorId,
            Reason: kind == "reminder_signal"
                ? "reminder_signal_written_after_confirm_missing_due_time"
                : "plan_signal_written_after_confirm");
    }

    private static Phase80ConfirmWriteExecutionResult Skipped(
        Phase80ConfirmExecutionRequest request,
        string reason)
    {
        return new Phase80ConfirmWriteExecutionResult(
            Success: false,
            Status: "skipped",
            Target: request.Plan.Target,
            ResourcePath: null,
            WroteData: false,
            RealWritePath: false,
            ExecutorId: ExecutorId,
            Reason: reason);
    }

    private static string CleanTitle(string title, string actionType)
    {
        var value = title.Trim();
        var prefix = actionType switch
        {
            Phase80PendingActionRuntime.ReminderPreview => "提醒：",
            Phase80PendingActionRuntime.PlanPreview => "计划：",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(prefix) && value.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = value[prefix.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(value) ? "计划线索" : value;
    }

    private static string CleanContent(string summary, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(summary) ? fallback : summary.Trim();
        const string userInputPrefix = "用户输入：";
        if (value.StartsWith(userInputPrefix, StringComparison.Ordinal))
        {
            value = value[userInputPrefix.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
