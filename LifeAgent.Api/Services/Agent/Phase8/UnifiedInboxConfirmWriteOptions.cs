using Microsoft.Extensions.Configuration;

namespace LifeAgent.Api.Services.Agent.Phase8;

public sealed class UnifiedInboxConfirmWriteOptions
{
    public const string SectionName = "UnifiedInbox:ConfirmWrites";
    public const string AllowLifeEventWritesEnvName = "UNIFIED_INBOX_ALLOW_LIFE_EVENT_WRITES";
    public const string AllowReminderWritesEnvName = "UNIFIED_INBOX_ALLOW_REMINDER_WRITES";

    public bool AllowLifeEventWrites { get; init; } = true;
    public bool AllowReminderWrites { get; init; }

    public static UnifiedInboxConfirmWriteOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new UnifiedInboxConfirmWriteOptions
        {
            AllowLifeEventWrites = ReadBool(
                configuration,
                section,
                AllowLifeEventWritesEnvName,
                "AllowLifeEventWrites",
                defaultValue: true),
            AllowReminderWrites = ReadBool(
                configuration,
                section,
                AllowReminderWritesEnvName,
                "AllowReminderWrites",
                defaultValue: false)
        };
    }

    private static bool ReadBool(
        IConfiguration configuration,
        IConfiguration section,
        string environmentKey,
        string sectionKey,
        bool defaultValue)
    {
        var raw = configuration[environmentKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = section[sectionKey];
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
