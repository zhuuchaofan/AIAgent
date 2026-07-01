using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.Memories;

public sealed class MemoryContextProviderOptions
{
    public bool EnableMemoryRetrieval { get; init; }
    public bool EnableMemoryContextInAgent { get; init; }
    public int MaxResults { get; init; }
    public IReadOnlyList<string> IntentAllowlist { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UserAllowlist { get; init; } = Array.Empty<string>();

    public static MemoryContextProviderOptions Disabled { get; } = new();

    public static MemoryContextProviderOptions FromConfiguration(IConfiguration configuration)
    {
        return new MemoryContextProviderOptions
        {
            EnableMemoryRetrieval = ReadBool(configuration, "ENABLE_MEMORY_RETRIEVAL"),
            EnableMemoryContextInAgent = ReadBool(configuration, "ENABLE_MEMORY_CONTEXT_IN_AGENT"),
            MaxResults = ReadInt(configuration, "MEMORY_RETRIEVAL_MAX_RESULTS"),
            IntentAllowlist = ReadCsv(configuration, "MEMORY_RETRIEVAL_INTENT_ALLOWLIST"),
            UserAllowlist = ReadCsv(configuration, "MEMORY_RETRIEVAL_USER_ALLOWLIST")
        };
    }

    private static bool ReadBool(IConfiguration configuration, string key)
    {
        return string.Equals(configuration[key], "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuration[key], "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuration[key], "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(IConfiguration configuration, string key)
    {
        return int.TryParse(configuration[key], out var value) ? value : 0;
    }

    private static IReadOnlyList<string> ReadCsv(IConfiguration configuration, string key)
    {
        return (configuration[key] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
