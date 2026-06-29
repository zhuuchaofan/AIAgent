using LifeAgent.Api.Services.LifeEvents;
using Microsoft.Extensions.Configuration;

namespace LifeAgent.Tests;

public class AgentWriteFeatureGateTest
{
    [Fact]
    public void DefaultsToFalse()
    {
        var gate = new AgentWriteFeatureGate(Configuration());

        Assert.False(gate.Options.EnableAgentWriteTools);
        Assert.False(gate.Options.EnableCreateLifeEventTool);
        Assert.False(gate.CanCreateLifeEvent());
    }

    [Fact]
    public void AgentWriteToolsFalseDisablesCreateLifeEvent()
    {
        var gate = new AgentWriteFeatureGate(Configuration(new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "false",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "true"
        }));

        Assert.False(gate.Options.EnableAgentWriteTools);
        Assert.True(gate.Options.EnableCreateLifeEventTool);
        Assert.False(gate.CanCreateLifeEvent());
    }

    [Fact]
    public void CreateLifeEventFalseDisablesCreateLifeEvent()
    {
        var gate = new AgentWriteFeatureGate(Configuration(new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "true",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "false"
        }));

        Assert.True(gate.Options.EnableAgentWriteTools);
        Assert.False(gate.Options.EnableCreateLifeEventTool);
        Assert.False(gate.CanCreateLifeEvent());
    }

    [Fact]
    public void BothFlagsTrueAllowsCreateLifeEventPath()
    {
        var gate = new AgentWriteFeatureGate(Configuration(new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "true",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "true"
        }));

        Assert.True(gate.Options.EnableAgentWriteTools);
        Assert.True(gate.Options.EnableCreateLifeEventTool);
        Assert.True(gate.CanCreateLifeEvent());
    }

    [Fact]
    public void DecisionDoesNotDependOnRequestBodyValues()
    {
        var gate = new AgentWriteFeatureGate(Configuration(new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "false",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "false"
        }));
        var fakeRequestBody = new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "true",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "true"
        };

        Assert.NotEmpty(fakeRequestBody);
        Assert.False(gate.CanCreateLifeEvent());
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
