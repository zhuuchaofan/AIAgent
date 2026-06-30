using System.Text.Json;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.LifeEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeAgent.Tests;

public class AgentLifeEventConfirmationWriteCoordinatorTest
{
    [Fact]
    public async Task FlagsFalse_ExistingConfirmRemainsPreviewOnly()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);

        var response = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "confirm");

        Assert.True(response.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.False(pending.WroteData);
        Assert.False(pending.WriteCompleted);
        Assert.Null(pending.CreatedResourceId);
        AssertPreviewOnly(response.Result);
    }

    [Fact]
    public async Task FlagsTrueTestPath_WritesLifeEventAndRecordsCreatedResource()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.True(response.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.True(pending.WroteData);
        Assert.True(pending.WriteCompleted);
        Assert.Equal("life_event", pending.CreatedResourceType);
        Assert.Equal("evt_" + pending.ProposedAction.ActionId, pending.CreatedResourceId);
        AssertWriteResult(response.Result, pending.CreatedResourceId!, idempotent: false);
        Assert.Equal(1, lifeEvents.CallCount);
    }

    [Fact]
    public async Task RepeatedConfirmReturnsSameCreatedResourceAndDoesNotWriteAgain()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var first = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);
        var second = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, lifeEvents.CallCount);
        Assert.Equal(pending.CreatedResourceId, lifeEvents.CreatedEvents.Single().Id);
        AssertWriteResult(second.Result, pending.CreatedResourceId!, idempotent: true);
    }

    [Fact]
    public async Task WriteFailureDoesNotConfirmPendingAction()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        var lifeEvents = new RecordingLifeEventService
        {
            ErrorToThrow = new InvalidOperationException("firestore unavailable")
        };
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.False(response.Success);
        Assert.Equal("write_failed", response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.False(pending.WroteData);
        Assert.False(pending.WriteCompleted);
        Assert.Null(pending.ConfirmedAt);
        Assert.Null(pending.CreatedResourceId);
        Assert.Equal(1, lifeEvents.CallCount);
        AssertPreviewOnly(response.Result);
    }

    [Fact]
    public async Task InvalidPayloadDoesNotWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "title",
            "summary",
            new
            {
                type = "cat",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次。",
                userId = "payload_user"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.False(response.Success);
        Assert.Equal("invalid_payload", response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.Equal(0, lifeEvents.CallCount);
        AssertPreviewOnly(response.Result);
    }

    [Fact]
    public async Task CancelledActionDoesNotWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "cancel");
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.False(response.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, response.Status);
        Assert.Equal(0, lifeEvents.CallCount);
        Assert.False(pending.WroteData);
    }

    [Fact]
    public async Task ExpiredActionDoesNotWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store, ttl: TimeSpan.FromSeconds(-1));
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.False(response.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Expired, response.Status);
        Assert.Equal(0, lifeEvents.CallCount);
        Assert.False(pending.WroteData);
    }

    [Fact]
    public async Task CrossUserActionDoesNotWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        var lifeEvents = new RecordingLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var response = await coordinator.ConfirmCreateLifeEventAsync("user_b", pending.ProposedAction.ActionId);

        Assert.False(response.Success);
        Assert.Equal("not_found", response.Status);
        Assert.Equal(0, lifeEvents.CallCount);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
    }

    [Fact]
    public async Task LifeEventServiceUsesStableEventIdDerivedFromActionId()
    {
        var service = new InMemoryAgentLifeEventService();
        var first = await service.CreateFromAgentConfirmationAsync("user_a", "agent_action_1", ValidRequest());
        var second = await service.CreateFromAgentConfirmationAsync("user_a", "agent_action_1", ValidRequest());

        Assert.Equal("evt_agent_action_1", first.Id);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task CoordinatorLogsWriteSuccessFailureAndIdempotentWithoutPayloadSecrets()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await CreatePendingLifeEventAction(store);
        var lifeEvents = new RecordingLifeEventService();
        var logger = new RecordingLogger<AgentLifeEventConfirmationWriteCoordinator>();
        var coordinator = CreateCoordinator(store, lifeEvents, logger);

        var first = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);
        var second = await coordinator.ConfirmCreateLifeEventAsync("user_a", pending.ProposedAction.ActionId);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Contains(logger.Messages, item => item.Contains("Agent life_event write started", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, item => item.Contains("Agent life_event write completed", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, item => item.Contains("Idempotent=True", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Messages, item => item.Contains("今天黑猫吐了一次", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, item => item.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Messages, item => item.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CoordinatorLogsInvalidPayloadAndWriteFailedWithoutFullPayload()
    {
        var invalidStore = new InMemoryPendingAgentActionStore();
        var invalidPending = await invalidStore.CreateAsync(
            "user_a",
            "create_life_event",
            "title",
            "summary",
            new
            {
                type = "cat",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次。",
                token = "super-secret-token"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var invalidLogger = new RecordingLogger<AgentLifeEventConfirmationWriteCoordinator>();
        var invalidCoordinator = CreateCoordinator(invalidStore, new RecordingLifeEventService(), invalidLogger);

        var invalid = await invalidCoordinator.ConfirmCreateLifeEventAsync("user_a", invalidPending.ProposedAction.ActionId);

        Assert.False(invalid.Success);
        Assert.Equal("invalid_payload", invalid.Status);
        Assert.Contains(invalidLogger.Messages, item => item.Contains("invalid_payload", StringComparison.Ordinal));
        Assert.DoesNotContain(invalidLogger.Messages, item => item.Contains("super-secret-token", StringComparison.Ordinal));

        var failedStore = new InMemoryPendingAgentActionStore();
        var failedPending = await CreatePendingLifeEventAction(failedStore);
        var failedLogger = new RecordingLogger<AgentLifeEventConfirmationWriteCoordinator>();
        var failedCoordinator = CreateCoordinator(
            failedStore,
            new RecordingLifeEventService { ErrorToThrow = new InvalidOperationException("firestore unavailable") },
            failedLogger);

        var failed = await failedCoordinator.ConfirmCreateLifeEventAsync("user_a", failedPending.ProposedAction.ActionId);

        Assert.False(failed.Success);
        Assert.Equal("write_failed", failed.Status);
        Assert.Contains(failedLogger.Messages, item => item.Contains("write_failed", StringComparison.Ordinal));
        Assert.DoesNotContain(failedLogger.Messages, item => item.Contains("今天黑猫吐了一次", StringComparison.Ordinal));
    }

    private static Task<PendingAgentAction> CreatePendingLifeEventAction(
        InMemoryPendingAgentActionStore store,
        TimeSpan? ttl = null)
    {
        return store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。",
                structuredData = new
                {
                    tags = new[] { "猫", "健康" },
                    catName = "黑猫",
                    importance = 2
                }
            },
            "medium",
            ttl ?? TimeSpan.FromMinutes(10));
    }

    private static CreateLifeEventRequest ValidRequest()
    {
        return new CreateLifeEventRequest
        {
            Type = "cat",
            Title = "黑猫呕吐",
            Content = "今天黑猫吐了一次，精神还可以。"
        };
    }

    private static AgentLifeEventConfirmationWriteCoordinator CreateCoordinator(
        IPendingAgentActionStore pendingActions,
        IAgentLifeEventService lifeEventService,
        ILogger<AgentLifeEventConfirmationWriteCoordinator>? logger = null)
    {
        return new AgentLifeEventConfirmationWriteCoordinator(
            pendingActions,
            lifeEventService,
            logger ?? NullLogger<AgentLifeEventConfirmationWriteCoordinator>.Instance);
    }

    private static void AssertPreviewOnly(object? result)
    {
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"previewOnly\":true", json);
        Assert.Contains("\"wroteData\":false", json);
    }

    private static void AssertWriteResult(object? result, string createdResourceId, bool idempotent)
    {
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"previewOnly\":false", json);
        Assert.Contains("\"wroteData\":true", json);
        Assert.Contains("\"createdResourceType\":\"life_event\"", json);
        Assert.Contains($"\"createdResourceId\":\"{createdResourceId}\"", json);
        Assert.Contains($"\"idempotent\":{idempotent.ToString().ToLowerInvariant()}", json);
    }

    private sealed class RecordingLifeEventService : IAgentLifeEventService
    {
        public int CallCount { get; private set; }
        public List<LifeEvent> CreatedEvents { get; } = new();
        public Exception? ErrorToThrow { get; set; }

        public Task<LifeEvent> CreateFromAgentConfirmationAsync(
            string authenticatedUserId,
            string agentActionId,
            CreateLifeEventRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ErrorToThrow is not null)
            {
                throw ErrorToThrow;
            }

            var lifeEvent = AgentLifeEventFactory.Create(authenticatedUserId, agentActionId, request);
            CreatedEvents.Add(lifeEvent);
            return Task.FromResult(lifeEvent);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
