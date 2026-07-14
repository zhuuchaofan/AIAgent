namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed class Phase80ConfirmWriteExecutorRouter : IPhase80ConfirmWriteExecutor
{
    private readonly IReadOnlyList<IPhase80ConfirmWriteExecutor> _executors;

    public Phase80ConfirmWriteExecutorRouter(params IPhase80ConfirmWriteExecutor[] executors)
    {
        _executors = executors;
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        foreach (var executor in _executors)
        {
            var readiness = executor.GetReadiness(plan);
            if (readiness.ExecutionReady && readiness.RealPathReady)
            {
                return readiness;
            }
        }

        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: false,
            RealPathReady: false,
            ExecutorId: "phase80_confirm_write_executor_router",
            Reason: "no_executor_supports_confirm_target");
    }

    public Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        foreach (var executor in _executors)
        {
            var readiness = executor.GetReadiness(request.Plan);
            if (readiness.ExecutionReady && readiness.RealPathReady)
            {
                return executor.ExecuteAsync(request, cancellationToken);
            }
        }

        return Task.FromResult(new Phase80ConfirmWriteExecutionResult(
            Success: false,
            Status: "skipped",
            Target: request.Plan.Target,
            ResourcePath: null,
            WroteData: false,
            RealWritePath: false,
            ExecutorId: "phase80_confirm_write_executor_router",
            Reason: "no_executor_supports_confirm_target"));
    }
}
