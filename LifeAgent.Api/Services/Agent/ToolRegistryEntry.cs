namespace LifeAgent.Api.Services.Agent;

public sealed record ToolRegistryEntry(
    string ToolName,
    string DisplayName,
    string ToolVersion,
    string OwnerDomain,
    string Description,
    string Category,
    string CapabilityType,
    string RiskLevel,
    bool AuthRequired,
    bool UserScoped,
    bool ReadsData,
    bool WritesData,
    bool ExternalSideEffect,
    bool ConfirmationRequired,
    bool SupportsPreview,
    bool SupportsConfirm,
    bool SupportsDirectExecute,
    bool SupportsIdempotency,
    string? FeatureFlagKey,
    string? ReleaseGate,
    bool TraceRequired,
    bool AuditRequired,
    string TimeoutPolicy,
    string RetryPolicy,
    string ErrorContractVersion,
    string InputSchemaRef,
    string OutputSchemaRef,
    string? PreviewSchemaRef,
    string? ConfirmationSchemaRef)
{
    public bool IsReadOnlyEligible =>
        (Category is ToolCategories.ReadOnlyRetrieval or ToolCategories.DiagnosticsOnly or ToolCategories.SystemInternal) &&
        ReadsData &&
        !WritesData &&
        !ExternalSideEffect &&
        !ConfirmationRequired &&
        !SupportsConfirm &&
        SupportsDirectExecute;

    public static ToolRegistryEntry FromTool(IAgentTool tool)
    {
        var category = tool.Risk == AgentToolRisk.Read || tool.Risk == AgentToolRisk.Compute
            ? ToolCategories.ReadOnlyRetrieval
            : ToolCategories.ConfirmRequiredWrite;

        return new ToolRegistryEntry(
            ToolName: tool.Name,
            DisplayName: tool.Name,
            ToolVersion: "1.0",
            OwnerDomain: "agent",
            Description: tool.Description,
            Category: category,
            CapabilityType: tool.Risk == AgentToolRisk.Compute ? "retrieval_answer" : "retrieval",
            RiskLevel: tool.Risk is AgentToolRisk.Read or AgentToolRisk.Compute ? "medium_sensitive_read" : "high_internal_write",
            AuthRequired: true,
            UserScoped: true,
            ReadsData: tool.Risk is AgentToolRisk.Read or AgentToolRisk.Compute,
            WritesData: tool.Risk is AgentToolRisk.Write,
            ExternalSideEffect: tool.Risk is AgentToolRisk.External,
            ConfirmationRequired: tool.RequiresConfirmation,
            SupportsPreview: false,
            SupportsConfirm: false,
            SupportsDirectExecute: !tool.RequiresConfirmation,
            SupportsIdempotency: false,
            FeatureFlagKey: null,
            ReleaseGate: tool.Risk is AgentToolRisk.Write or AgentToolRisk.External ? "required" : null,
            TraceRequired: true,
            AuditRequired: tool.Risk is AgentToolRisk.Read or AgentToolRisk.Compute,
            TimeoutPolicy: "provider_default",
            RetryPolicy: "none",
            ErrorContractVersion: "phase7.2",
            InputSchemaRef: $"agent.tools.{tool.Name}.input.v1",
            OutputSchemaRef: $"agent.tools.{tool.Name}.output.v1",
            PreviewSchemaRef: null,
            ConfirmationSchemaRef: null);
    }
}

public static class ToolCategories
{
    public const string ReadOnlyRetrieval = "read_only_retrieval";
    public const string DiagnosticsOnly = "diagnostics_only";
    public const string SystemInternal = "system_internal";
    public const string ConfirmRequiredWrite = "confirm_required_write";
    public const string ExternalSideEffect = "external_side_effect";
}
