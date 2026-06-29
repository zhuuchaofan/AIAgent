using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.LifeEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAgent.Tests;

public class AgentLifeEventDiTest
{
    [Fact]
    public void AgentLifeEventWriteDependenciesResolveAsScopedServices()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<IAgentWriteFeatureGate>());
        Assert.NotNull(services.GetRequiredService<IPendingAgentActionStore>());
        Assert.NotNull(services.GetRequiredService<IAgentLifeEventWriter>());
        Assert.NotNull(services.GetRequiredService<IAgentLifeEventService>());
        Assert.NotNull(services.GetRequiredService<AgentLifeEventConfirmationWriteCoordinator>());
    }

    [Fact]
    public void AgentWriteFeatureGate_DefaultsToDisabledEvenWhenWriteServicesAreRegistered()
    {
        var provider = BuildProvider();
        var gate = provider.GetRequiredService<IAgentWriteFeatureGate>();

        Assert.False(gate.Options.EnableAgentWriteTools);
        Assert.False(gate.Options.EnableCreateLifeEventTool);
        Assert.False(gate.CanCreateLifeEvent());
    }

    [Fact]
    public async Task FlagsFalseConfirmDoesNotCallRegisteredLifeEventWriter()
    {
        var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var pendingActions = services.GetRequiredService<IPendingAgentActionStore>();
        var writer = (RecordingAgentLifeEventWriter)services.GetRequiredService<IAgentLifeEventWriter>();
        var pending = await pendingActions.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            pendingActions,
            services.GetRequiredService<IAgentWriteFeatureGate>(),
            services.GetRequiredService<AgentLifeEventConfirmationWriteCoordinator>(),
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.False(pending.WroteData);
        Assert.False(pending.WriteCompleted);
        Assert.Equal(0, writer.WriteCount);
    }

    [Fact]
    public async Task FlagsTrueConfirmHasCompleteDependenciesForWritePath()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "true",
            [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "true"
        });
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var pendingActions = services.GetRequiredService<IPendingAgentActionStore>();
        var writer = (RecordingAgentLifeEventWriter)services.GetRequiredService<IAgentLifeEventWriter>();
        var pending = await pendingActions.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            pendingActions,
            services.GetRequiredService<IAgentWriteFeatureGate>(),
            services.GetRequiredService<AgentLifeEventConfirmationWriteCoordinator>(),
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.True(pending.WroteData);
        Assert.Equal("life_event", pending.CreatedResourceType);
        Assert.Equal(pending.CreatedResourceId, writer.LastEventId);
        Assert.Equal(1, writer.WriteCount);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? flags = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(flags ?? new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IAgentWriteFeatureGate, AgentWriteFeatureGate>();
        services.AddSingleton<IPendingAgentActionStore, InMemoryPendingAgentActionStore>();
        services.AddScoped<AgentLifeEventConfirmationWriteCoordinator>();
        services.AddScoped<IAgentLifeEventWriter, RecordingAgentLifeEventWriter>();
        services.AddScoped<IAgentLifeEventService, FirestoreAgentLifeEventService>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class RecordingAgentLifeEventWriter : IAgentLifeEventWriter
    {
        public int WriteCount { get; private set; }
        public string? LastEventId { get; private set; }
        public LifeEvent? LastEvent { get; private set; }

        public Task WriteAsync(
            string authenticatedUserId,
            string eventId,
            LifeEvent lifeEvent,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastEventId = eventId;
            LastEvent = lifeEvent;
            return Task.CompletedTask;
        }
    }
}
