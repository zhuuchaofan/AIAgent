namespace LifeAgent.Api.Services.Agent;

public sealed record AgentExecutionContract(
    string Intent,
    double IntentConfidence,
    string ActionType,
    bool RequiresConfirmation,
    bool IsFallback,
    string? FallbackReason);
