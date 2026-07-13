namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed class Phase80NoOpConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    public static Phase80NoOpConfirmWriteExecutor Instance { get; } = new();

    private Phase80NoOpConfirmWriteExecutor()
    {
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: false,
            RealPathReady: false,
            ExecutorId: "noop_confirm_write_executor",
            Reason: "confirm_write_policy_enabled_but_executor_not_connected");
    }

    public Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Phase80ConfirmWriteExecutionResult(
            Success: false,
            Status: "skipped",
            Target: request.Plan.Target,
            ResourcePath: null,
            WroteData: false,
            RealWritePath: false,
            ExecutorId: "noop_confirm_write_executor",
            Reason: "noop_executor_never_writes"));
    }
}
