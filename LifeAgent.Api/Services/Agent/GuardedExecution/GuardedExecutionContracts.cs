using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Api.Services.Agent.GuardedExecution;

public sealed record GuardedExecutionRequest(
    string UserSubjectRef,
    string PendingActionId,
    string ConfirmationId,
    string ToolId,
    string ToolVersion,
    string InputHash,
    string PreviewHash,
    string ConfirmationHash,
    string IdempotencyKeyHash,
    bool WriteIntent,
    bool ExternalCall,
    string RiskLevel,
    string TraceId,
    string SchemaVersion = "phase7.10.guarded_execution.v1");

public sealed record GuardedExecutionResponse(
    bool Success,
    string Status,
    string PendingActionId,
    string? ConfirmationId,
    string? PreviewId,
    string? ToolId,
    string? ToolVersion,
    string RiskLevel,
    GuardDecisionType Decision,
    ReleaseGateDecision ReleaseGateDecision,
    string? BlockedReason,
    string TraceId,
    string? AuditEventRef,
    bool ExecutionReady,
    bool Executed,
    bool WroteData,
    bool ExternalCallMade,
    string SchemaVersion = "phase7.10.guarded_execution.v1");

public sealed record GuardValidationResult(
    bool Success,
    GuardDecisionType Decision,
    string Status,
    string SanitizedReason);

public sealed record GuardEvaluationContext(
    PendingActionRecord PendingAction,
    ReleaseGateDecision ReleaseGateDecision,
    GuardValidationResult ValidationResult);
