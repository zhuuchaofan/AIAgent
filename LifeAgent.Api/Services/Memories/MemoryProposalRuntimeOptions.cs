using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryProposalRuntimeOptions
{
    public bool EnableMemoryProposalRuntime { get; init; }
    public bool EnableMemoryProposalGuard { get; init; }
    public IReadOnlyList<string> UserAllowlist { get; init; } = Array.Empty<string>();

    public static MemoryProposalRuntimeOptions Disabled { get; } = new();

    public bool IsEnabledForUser(string userId)
    {
        if (!EnableMemoryProposalRuntime || !EnableMemoryProposalGuard)
        {
            return false;
        }

        return UserAllowlist.Count == 0 ||
               UserAllowlist.Contains(userId, StringComparer.OrdinalIgnoreCase);
    }

    public static MemoryProposalRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        return new MemoryProposalRuntimeOptions
        {
            EnableMemoryProposalRuntime = ReadBool(configuration, "ENABLE_MEMORY_PROPOSAL_RUNTIME"),
            EnableMemoryProposalGuard = ReadBool(configuration, "ENABLE_MEMORY_PROPOSAL_GUARD"),
            UserAllowlist = ReadCsv(configuration, "MEMORY_PROPOSAL_USER_ALLOWLIST")
        };
    }

    private static bool ReadBool(IConfiguration configuration, string key)
    {
        return string.Equals(configuration[key], "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuration[key], "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuration[key], "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadCsv(IConfiguration configuration, string key)
    {
        return (configuration[key] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
