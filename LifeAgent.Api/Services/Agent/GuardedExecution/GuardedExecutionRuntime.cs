using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Api.Services.Agent.GuardedExecution;

public sealed class GuardedExecutionRuntime : IGuardedExecutionRuntime
{
    private readonly IPendingActionStore _pendingActions;
    private readonly IReleaseGateEvaluator _releaseGateEvaluator;

    public GuardedExecutionRuntime(
        IPendingActionStore pendingActions,
        IReleaseGateEvaluator? releaseGateEvaluator = null)
    {
        _pendingActions = pendingActions;
        _releaseGateEvaluator = releaseGateEvaluator ?? new DenyAllReleaseGateEvaluator();
    }

    public async Task<GuardedExecutionResponse> EvaluateExecutionReadinessAsync(
        GuardedExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var pending = await _pendingActions.GetByIdAsync(
            request.UserSubjectRef,
            request.PendingActionId,
            cancellationToken);
        if (pending is null)
        {
            return Blocked(request, null, GuardDecisionType.RejectCrossUser, "not_found", "not_found", ReleaseGateDecision.Denied());
        }

        var releaseGate = await _releaseGateEvaluator.EvaluateAsync(
            new ReleaseGateEvaluationRequest(
                pending.ToolId,
                pending.ToolVersion,
                pending.ActionType,
                request.RiskLevel,
                request.WriteIntent,
                request.ExternalCall,
                request.UserSubjectRef),
            cancellationToken);

        var validation = Validate(request, pending, releaseGate);
        if (!validation.Success)
        {
            await RecordGuardDecisionAsync(request, pending, validation, cancellationToken);
            return Blocked(request, pending, validation.Decision, validation.Status, validation.SanitizedReason, releaseGate);
        }

        var update = await _pendingActions.RecordGuardDecisionReferenceAsync(
            request.UserSubjectRef,
            pending.PendingActionId,
            $"guard:{request.TraceId}:{validation.Decision}",
            PendingActionStatus.ExecutionReady,
            auditEventRef: $"audit:{request.TraceId}:execution_readiness_check_passed",
            cancellationToken: cancellationToken);
        var updated = update.Record ?? pending;

        return new GuardedExecutionResponse(
            Success: true,
            Status: PendingActionStatus.ExecutionReady,
            PendingActionId: updated.PendingActionId,
            ConfirmationId: updated.ConfirmationId,
            PreviewId: updated.PreviewId,
            ToolId: updated.ToolId,
            ToolVersion: updated.ToolVersion,
            RiskLevel: updated.RiskLevel,
            Decision: GuardDecisionType.AllowExecutionReady,
            ReleaseGateDecision: releaseGate,
            BlockedReason: null,
            TraceId: request.TraceId,
            AuditEventRef: $"audit:{request.TraceId}:execution_ready_marked",
            ExecutionReady: true,
            Executed: false,
            WroteData: false,
            ExternalCallMade: false);
    }

    private static GuardValidationResult Validate(
        GuardedExecutionRequest request,
        PendingActionRecord pending,
        ReleaseGateDecision releaseGate)
    {
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow || pending.Status == PendingActionStatus.Expired)
        {
            return Reject(GuardDecisionType.RejectExpired, "expired", PendingActionStatus.Expired);
        }

        if (pending.Status == PendingActionStatus.Cancelled)
        {
            return Reject(GuardDecisionType.RejectCancelled, "cancelled");
        }

        if (pending.Status != PendingActionStatus.Confirmed)
        {
            return Reject(GuardDecisionType.AllowConfirmationOnly, "confirmation_required", PendingActionStatus.ExecutionBlocked);
        }

        if (string.IsNullOrWhiteSpace(pending.ConfirmationId) ||
            !string.Equals(pending.ConfirmationId, request.ConfirmationId, StringComparison.Ordinal))
        {
            return Reject(GuardDecisionType.RejectMissingConfirmation, "missing_confirmation");
        }

        if (!string.Equals(pending.ToolId, request.ToolId, StringComparison.Ordinal) ||
            !string.Equals(pending.ToolVersion, request.ToolVersion, StringComparison.Ordinal))
        {
            return Reject(GuardDecisionType.RejectToolVersionMismatch, "tool_version_mismatch");
        }

        if (!string.Equals(pending.InputHash, request.InputHash, StringComparison.Ordinal) ||
            !string.Equals(pending.PreviewHash, request.PreviewHash, StringComparison.Ordinal))
        {
            return Reject(GuardDecisionType.RejectHashMismatch, "hash_mismatch");
        }

        if (pending.ValidationSnapshot.TryGetValue("confirmationHash", out var confirmationHash) &&
            !string.Equals(confirmationHash, request.ConfirmationHash, StringComparison.Ordinal))
        {
            return Reject(GuardDecisionType.RejectHashMismatch, "confirmation_hash_mismatch");
        }

        if (!string.Equals(pending.IdempotencyKeyHash, request.IdempotencyKeyHash, StringComparison.Ordinal))
        {
            return Reject(GuardDecisionType.RejectReplay, "idempotency_mismatch");
        }

        if (request.ExternalCall && !releaseGate.AllowsExternalCall)
        {
            return Reject(GuardDecisionType.RejectExternalCall, "external_call_blocked");
        }

        if (request.WriteIntent && !releaseGate.AllowsWriteIntent)
        {
            return Reject(GuardDecisionType.RejectWriteIntent, "write_intent_blocked");
        }

        if (request.RiskLevel is "high_internal_write" or "high_external_side_effect" or "critical_release_gated" &&
            !releaseGate.AllowsHighRisk)
        {
            return Reject(GuardDecisionType.RequireReleaseGate, "release_gate_required");
        }

        if (!releaseGate.AllowsExecution)
        {
            return Reject(GuardDecisionType.RequireReleaseGate, releaseGate.Reason);
        }

        return new GuardValidationResult(true, GuardDecisionType.AllowExecutionReady, PendingActionStatus.ExecutionReady, "allowed_for_future_readiness");
    }

    private async Task RecordGuardDecisionAsync(
        GuardedExecutionRequest request,
        PendingActionRecord pending,
        GuardValidationResult validation,
        CancellationToken cancellationToken)
    {
        var status = validation.Status == PendingActionStatus.Expired
            ? PendingActionStatus.Expired
            : PendingActionStatus.ExecutionBlocked;
        await _pendingActions.RecordGuardDecisionReferenceAsync(
            request.UserSubjectRef,
            pending.PendingActionId,
            $"guard:{request.TraceId}:{validation.Decision}",
            status,
            validation.SanitizedReason,
            $"audit:{request.TraceId}:execution_blocked",
            cancellationToken);
    }

    private static GuardedExecutionResponse Blocked(
        GuardedExecutionRequest request,
        PendingActionRecord? pending,
        GuardDecisionType decision,
        string status,
        string reason,
        ReleaseGateDecision releaseGate)
    {
        return new GuardedExecutionResponse(
            Success: false,
            Status: status,
            PendingActionId: request.PendingActionId,
            ConfirmationId: pending?.ConfirmationId,
            PreviewId: pending?.PreviewId,
            ToolId: pending?.ToolId,
            ToolVersion: pending?.ToolVersion,
            RiskLevel: pending?.RiskLevel ?? request.RiskLevel,
            Decision: decision,
            ReleaseGateDecision: releaseGate,
            BlockedReason: reason,
            TraceId: request.TraceId,
            AuditEventRef: $"audit:{request.TraceId}:execution_blocked",
            ExecutionReady: false,
            Executed: false,
            WroteData: false,
            ExternalCallMade: false);
    }

    private static GuardValidationResult Reject(
        GuardDecisionType decision,
        string reason,
        string status = PendingActionStatus.ExecutionBlocked)
    {
        return new GuardValidationResult(false, decision, status, reason);
    }
}
