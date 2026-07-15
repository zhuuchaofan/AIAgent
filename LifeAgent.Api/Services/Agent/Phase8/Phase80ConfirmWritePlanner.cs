namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed partial class Phase80PendingActionRuntime
{
    internal static Phase80ConfirmExecutionPlan ResolveConfirmExecutionPlan(string actionType)
    {
        return ResolveConfirmExecutionPlan(actionType, Phase80ConfirmWritePolicy.DefaultPreviewOnly());
    }

    internal static Phase80ConfirmExecutionPlan ResolveConfirmExecutionPlan(
        string actionType,
        Phase80ConfirmWritePolicy policy)
    {
        return actionType switch
        {
            LifeRecordPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetLifeEvents,
                WriteEnabled: policy.AllowLifeEventWrites,
                MemoryCandidateOnly: true,
                Reason: policy.AllowLifeEventWrites
                    ? "life_record_confirm_write_allowed_by_policy"
                    : "life_record_confirm_write_disabled_until_beta_gate"),
            ReminderPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetReminders,
                WriteEnabled: policy.AllowReminderWrites,
                MemoryCandidateOnly: true,
                Reason: policy.AllowReminderWrites
                    ? "reminder_confirm_write_allowed_by_policy"
                    : "reminder_confirm_write_disabled_until_beta_gate"),
            PlanPreview => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetPlanSignals,
                WriteEnabled: policy.AllowPlanSignalWrites,
                MemoryCandidateOnly: true,
                Reason: policy.AllowPlanSignalWrites
                    ? "plan_signal_confirm_write_allowed_by_policy"
                    : "plan_confirm_preview_only_until_planning_store_exists"),
            _ => new Phase80ConfirmExecutionPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                MemoryCandidateOnly: true,
                Reason: "unknown_action_type_preview_only")
        };
    }

    internal static Phase80ConfirmWriteDecision ResolveConfirmWriteDecision(Phase80ConfirmExecutionPlan plan)
    {
        return ResolveConfirmWriteDecision(plan, Phase80NoOpConfirmWriteExecutor.Instance);
    }

    internal static Phase80ConfirmWriteDecision ResolveConfirmWriteDecision(
        Phase80ConfirmExecutionPlan plan,
        IPhase80ConfirmWriteExecutor executor)
    {
        if (!plan.WriteEnabled)
        {
            return new Phase80ConfirmWriteDecision(
                ExecutionReady: false,
                RealPathReady: false,
                ExecutorId: "none",
                Reason: "confirm_write_disabled_by_policy");
        }

        var readiness = executor.GetReadiness(plan);
        return new Phase80ConfirmWriteDecision(
            ExecutionReady: readiness.ExecutionReady,
            RealPathReady: readiness.RealPathReady,
            ExecutorId: readiness.ExecutorId,
            Reason: readiness.Reason);
    }

    internal static Phase80ConfirmWriteInvocationGate ResolveConfirmWriteInvocationGate(
        Phase80ConfirmExecutionPlan plan,
        Phase80ConfirmWriteDecision decision)
    {
        if (!plan.WriteEnabled)
        {
            return new Phase80ConfirmWriteInvocationGate(
                ShouldInvoke: false,
                Reason: "confirm_write_policy_disabled");
        }

        if (!decision.ExecutionReady || !decision.RealPathReady)
        {
            return new Phase80ConfirmWriteInvocationGate(
                ShouldInvoke: false,
                Reason: "confirm_write_executor_not_ready");
        }

        return new Phase80ConfirmWriteInvocationGate(
            ShouldInvoke: true,
            Reason: "confirm_write_all_gates_ready");
    }

    internal async Task<Phase80ConfirmWriteExecutionResult> TryExecuteConfirmWriteAsync(
        Phase80ConfirmExecutionRequest request,
        Phase80ConfirmWriteDecision decision,
        CancellationToken cancellationToken = default)
    {
        var gate = ResolveConfirmWriteInvocationGate(request.Plan, decision);
        if (!gate.ShouldInvoke)
        {
            _logger.LogInformation(
                "LifeOS confirm write skipped. UserSubjectRef={UserSubjectRef} ActionId={ActionId} ActionType={ActionType} Target={Target} ExecutorId={ExecutorId} Reason={Reason}",
                request.UserId,
                request.ActionId,
                request.ActionType,
                request.Plan.Target,
                decision.ExecutorId,
                gate.Reason);

            return new Phase80ConfirmWriteExecutionResult(
                Success: false,
                Status: "skipped",
                Target: request.Plan.Target,
                ResourcePath: null,
                WroteData: false,
                RealWritePath: false,
                ExecutorId: decision.ExecutorId,
                Reason: gate.Reason);
        }

        var result = await _confirmWriteExecutor.ExecuteAsync(request, cancellationToken);
        _logger.LogInformation(
            "LifeOS confirm write completed. UserSubjectRef={UserSubjectRef} ActionId={ActionId} ActionType={ActionType} Target={Target} WroteData={WroteData} RealWritePath={RealWritePath} ExecutorId={ExecutorId} Reason={Reason}",
            request.UserId,
            request.ActionId,
            request.ActionType,
            result.Target,
            result.WroteData,
            result.RealWritePath,
            result.ExecutorId,
            result.Reason);

        return result;
    }

    internal static Phase80MemoryPlan ResolveMemoryPlan(string actionType)
    {
        return actionType switch
        {
            LifeRecordPreview or ReminderPreview or PlanPreview => new Phase80MemoryPlan(
                Target: MemoryCandidateTarget,
                WriteEnabled: false,
                RequiresDedupe: true,
                RequiresMerge: true,
                RequiresConfirmation: true,
                Reason: "memory_candidate_only_until_dedupe_merge_confirm"),
            _ => new Phase80MemoryPlan(
                Target: ConfirmTargetNone,
                WriteEnabled: false,
                RequiresDedupe: true,
                RequiresMerge: true,
                RequiresConfirmation: true,
                Reason: "unknown_action_type_no_memory_write")
        };
    }
}
