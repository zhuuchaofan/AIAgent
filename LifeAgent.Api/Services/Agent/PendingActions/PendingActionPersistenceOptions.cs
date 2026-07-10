using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public sealed record PendingActionPersistenceOptions
{
    public const string SectionName = "AgentRuntime:PendingActionStore";
    public const string ModeInMemory = "in_memory";
    public const string ModeFirestore = "firestore";
    public const string ModeDisabled = "disabled";

    public string Mode { get; init; } = ModeInMemory;
    public bool PreviewOnly { get; init; } = true;
    public bool AllowFirestore { get; init; }

    public bool UseFirestore =>
        AllowFirestore &&
        PreviewOnly &&
        string.Equals(Mode, ModeFirestore, StringComparison.OrdinalIgnoreCase);

    public string SafetyMode =>
        UseFirestore
            ? "personal_agent_v2_firestore_persistence_preview_only"
            : "personal_agent_v2_in_memory_preview_only";

    public static PendingActionPersistenceOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var mode = section["Mode"]
            ?? configuration["AGENT_PENDING_ACTION_STORE_MODE"]
            ?? Environment.GetEnvironmentVariable("AGENT_PENDING_ACTION_STORE_MODE")
            ?? ModeInMemory;
        var allowFirestore = ReadBool(section["AllowFirestore"])
            ?? ReadBool(configuration["AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE"])
            ?? ReadBool(Environment.GetEnvironmentVariable("AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE"))
            ?? false;
        var previewOnly = ReadBool(section["PreviewOnly"])
            ?? ReadBool(configuration["AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY"])
            ?? ReadBool(Environment.GetEnvironmentVariable("AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY"))
            ?? true;

        return new PendingActionPersistenceOptions
        {
            Mode = NormalizeMode(mode),
            AllowFirestore = allowFirestore,
            PreviewOnly = previewOnly
        };
    }

    private static string NormalizeMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            ModeFirestore => ModeFirestore,
            ModeDisabled => ModeDisabled,
            _ => ModeInMemory
        };
    }

    private static bool? ReadBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
}
