namespace LifeAgent.Api.Services.Agent.GuardedExecution;

public enum GuardDecisionType
{
    AllowPreviewOnly,
    AllowConfirmationOnly,
    AllowExecutionReady,
    BlockExecution,
    RequireReleaseGate,
    RequireRepreview,
    RejectStaleAction,
    RejectPolicyMismatch,
    RejectRiskLevel,
    RejectExternalCall,
    RejectWriteIntent,
    RejectReplay,
    RejectCrossUser,
    RejectExpired,
    RejectCancelled,
    RejectMissingConfirmation,
    RejectToolVersionMismatch,
    RejectHashMismatch
}
