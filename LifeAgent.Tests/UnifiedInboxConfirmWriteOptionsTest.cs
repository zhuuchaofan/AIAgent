using LifeAgent.Api.Services.Agent.Phase8;
using Microsoft.Extensions.Configuration;

namespace LifeAgent.Tests;

public class UnifiedInboxConfirmWriteOptionsTest
{
    [Fact]
    public void DefaultsKeepLifeEventWritesOnAndReminderWritesOff()
    {
        var options = UnifiedInboxConfirmWriteOptions.FromConfiguration(Configuration());

        Assert.True(options.AllowLifeEventWrites);
        Assert.False(options.AllowReminderWrites);
    }

    [Fact]
    public void SectionCanEnableReminderWrites()
    {
        var options = UnifiedInboxConfirmWriteOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["UnifiedInbox:ConfirmWrites:AllowReminderWrites"] = "true"
        }));

        Assert.True(options.AllowLifeEventWrites);
        Assert.True(options.AllowReminderWrites);
    }

    [Fact]
    public void EnvironmentStyleKeyOverridesSection()
    {
        var options = UnifiedInboxConfirmWriteOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["UnifiedInbox:ConfirmWrites:AllowReminderWrites"] = "false",
            [UnifiedInboxConfirmWriteOptions.AllowReminderWritesEnvName] = "true"
        }));

        Assert.True(options.AllowReminderWrites);
    }

    [Fact]
    public void SectionCanDisableLifeEventWritesForRollback()
    {
        var options = UnifiedInboxConfirmWriteOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["UnifiedInbox:ConfirmWrites:AllowLifeEventWrites"] = "false"
        }));

        Assert.False(options.AllowLifeEventWrites);
        Assert.False(options.AllowReminderWrites);
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
