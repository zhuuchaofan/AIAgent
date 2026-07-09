namespace LifeAgent.Api.Services.Agent.GuardedExecution;

public sealed record ReleaseGateEvaluationRequest(
    string ToolId,
    string ToolVersion,
    string ActionType,
    string RiskLevel,
    bool WriteIntent,
    bool ExternalCall,
    string UserSubjectRef);

public sealed record ReleaseGateDecision(
    bool AllowsExecution,
    bool AllowsWriteIntent,
    bool AllowsExternalCall,
    bool AllowsHighRisk,
    string Decision,
    string Reason)
{
    public static ReleaseGateDecision Denied(string reason = "release_gate_missing")
    {
        return new ReleaseGateDecision(false, false, false, false, "deny", reason);
    }

    public static ReleaseGateDecision AllowsReadinessOnly(string reason = "no_write_no_external")
    {
        return new ReleaseGateDecision(true, false, false, false, "readiness_only", reason);
    }
}

public interface IReleaseGateEvaluator
{
    Task<ReleaseGateDecision> EvaluateAsync(
        ReleaseGateEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DenyAllReleaseGateEvaluator : IReleaseGateEvaluator
{
    public Task<ReleaseGateDecision> EvaluateAsync(
        ReleaseGateEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.WriteIntent &&
            !request.ExternalCall &&
            request.RiskLevel is "low_readonly" or "medium_preview_only")
        {
            return Task.FromResult(ReleaseGateDecision.AllowsReadinessOnly());
        }

        return Task.FromResult(ReleaseGateDecision.Denied());
    }
}
