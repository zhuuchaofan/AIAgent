namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed record Phase80PersonalHomeIntentRoute(
    string Intent,
    string ActionType,
    string Disposition,
    string RiskLevel,
    bool RequiresPendingAction,
    string Reason,
    string Classifier = "rule_based_intent_classifier");

public sealed record Phase80CreatePendingActionRequest(
    string? Title,
    string? Summary,
    string? ActionType = null,
    string? ClientTimeZone = null);

public sealed record Phase80PendingActionRecord(
    string ActionId,
    string UserId,
    string Status,
    string Title,
    string Summary,
    string ActionType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    bool Executed,
    bool WroteData,
    bool ExecutionReady,
    string GuardDecision);

public sealed record Phase80PendingActionView(
    string ActionId,
    string Status,
    string Title,
    string Summary,
    string ActionType,
    string Intent,
    string Disposition,
    string RiskLevel,
    bool RequiresPendingAction,
    string RouteReason,
    string IntentClassifier,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    bool Executed,
    bool WroteData,
    bool ExecutionReady,
    string GuardDecision,
    string SafetyMode,
    bool LegacyConfirmEndpointUsed,
    bool RealWritePath,
    bool IsArchived,
    string ConfirmTarget,
    bool ConfirmWriteEnabled,
    bool ConfirmWriteExecutionReady,
    bool ConfirmWriteRealPathReady,
    string ConfirmWriteExecutorId,
    string ConfirmWriteDecisionReason,
    bool MemoryCandidateOnly,
    string ConfirmPlanReason,
    string MemoryTarget,
    bool MemoryWriteEnabled,
    bool MemoryRequiresDedupe,
    bool MemoryRequiresMerge,
    bool MemoryRequiresConfirmation,
    string Message);

public sealed record Phase80ConfirmExecutionPlan(
    string Target,
    bool WriteEnabled,
    bool MemoryCandidateOnly,
    string Reason);

public sealed record Phase80ConfirmWriteDecision(
    bool ExecutionReady,
    bool RealPathReady,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWriteInvocationGate(
    bool ShouldInvoke,
    string Reason);

public interface IPhase80ConfirmWriteExecutor
{
    Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan);

    Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record Phase80ConfirmExecutionRequest(
    string UserId,
    string ActionId,
    string ActionType,
    string Title,
    string Summary,
    Phase80ConfirmExecutionPlan Plan,
    string? ClientTimeZone = null);

public sealed record Phase80ConfirmWriteExecutionResult(
    bool Success,
    string Status,
    string Target,
    string? ResourcePath,
    bool WroteData,
    bool RealWritePath,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWriteExecutorReadiness(
    bool ExecutionReady,
    bool RealPathReady,
    string ExecutorId,
    string Reason);

public sealed record Phase80ConfirmWritePolicy(
    bool AllowLifeEventWrites,
    bool AllowReminderWrites,
    bool AllowPlanSignalWrites = false)
{
    public static Phase80ConfirmWritePolicy DefaultPreviewOnly()
    {
        return new Phase80ConfirmWritePolicy(
            AllowLifeEventWrites: false,
            AllowReminderWrites: false,
            AllowPlanSignalWrites: false);
    }
}

public sealed record Phase80MemoryPlan(
    string Target,
    bool WriteEnabled,
    bool RequiresDedupe,
    bool RequiresMerge,
    bool RequiresConfirmation,
    string Reason);

public sealed record Phase80PendingActionResult(
    bool Success,
    string Status,
    string Message,
    Phase80PendingActionView? Data)
{
    public static Phase80PendingActionResult Ok(string status, string message, Phase80PendingActionView data)
    {
        return new Phase80PendingActionResult(true, status, message, data);
    }

    public static Phase80PendingActionResult Fail(string status, string message, Phase80PendingActionView? data = null)
    {
        return new Phase80PendingActionResult(false, status, message, data);
    }
}
