namespace LifeAgent.Api.Services.Agent.GuardedExecution;

public interface IGuardedExecutionRuntime
{
    Task<GuardedExecutionResponse> EvaluateExecutionReadinessAsync(
        GuardedExecutionRequest request,
        CancellationToken cancellationToken = default);
}
