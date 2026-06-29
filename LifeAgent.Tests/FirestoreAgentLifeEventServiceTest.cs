using LifeAgent.Api.Models;
using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.LifeEvents;

namespace LifeAgent.Tests;

public class FirestoreAgentLifeEventServiceTest
{
    [Fact]
    public async Task CreateFromAgentConfirmation_WritesToAuthenticatedUserPath()
    {
        var writer = new FakeAgentLifeEventWriter();
        var service = new FirestoreAgentLifeEventService(writer);

        var lifeEvent = await service.CreateFromAgentConfirmationAsync(
            "auth_user",
            "agent_action_1",
            ValidRequest());

        Assert.Equal($"users/auth_user/life_events/{lifeEvent.Id}", writer.LastPath);
        Assert.Same(lifeEvent, writer.LastEvent);
        Assert.StartsWith("evt_", lifeEvent.Id);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_UsesAuthenticatedUserIdAndRejectsPayloadUserId()
    {
        var writer = new FakeAgentLifeEventWriter();
        var service = new FirestoreAgentLifeEventService(writer);
        var request = ValidRequest();
        request.UserId = "payload_user";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", request));

        Assert.Contains("system fields", error.Message);
        Assert.Null(writer.LastPath);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_OverwritesSystemFields()
    {
        var writer = new FakeAgentLifeEventWriter();
        var service = new FirestoreAgentLifeEventService(writer);

        var lifeEvent = await service.CreateFromAgentConfirmationAsync(
            "auth_user",
            "agent_action_1",
            ValidRequest());

        Assert.Equal("auth_user", lifeEvent.UserId);
        Assert.Equal("agent_confirmed", lifeEvent.Source);
        Assert.Equal("agent", lifeEvent.CreatedBy);
        Assert.Equal("agent_action_1", lifeEvent.AgentActionId);
        Assert.True(lifeEvent.CreatedAt > DateTime.MinValue);
        Assert.True(lifeEvent.UpdatedAt >= lifeEvent.CreatedAt);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_WritesOnlyValidatorAllowedStructuredData()
    {
        var writer = new FakeAgentLifeEventWriter();
        var service = new FirestoreAgentLifeEventService(writer);

        var lifeEvent = await service.CreateFromAgentConfirmationAsync(
            "auth_user",
            "agent_action_1",
            ValidRequest());

        Assert.Equal("黑猫", lifeEvent.StructuredData["catName"]);
        Assert.Equal(2, lifeEvent.StructuredData["importance"]);
        Assert.DoesNotContain("userId", lifeEvent.StructuredData.Keys);
        Assert.DoesNotContain("source", lifeEvent.StructuredData.Keys);
        Assert.DoesNotContain("createdBy", lifeEvent.StructuredData.Keys);
        Assert.DoesNotContain("agentActionId", lifeEvent.StructuredData.Keys);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_InvalidStructuredDataDoesNotWrite()
    {
        var writer = new FakeAgentLifeEventWriter();
        var service = new FirestoreAgentLifeEventService(writer);
        var request = ValidRequest();
        request.StructuredData["token"] = "bad";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", request));

        Assert.Contains("forbidden", error.Message);
        Assert.Null(writer.LastPath);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_WriteFailureThrowsControlledException()
    {
        var writer = new FakeAgentLifeEventWriter
        {
            ErrorToThrow = new InvalidOperationException("firestore unavailable")
        };
        var service = new FirestoreAgentLifeEventService(writer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", ValidRequest()));

        Assert.Contains("Failed to write Agent life event", error.Message);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static CreateLifeEventRequest ValidRequest()
    {
        return new CreateLifeEventRequest
        {
            Type = "cat",
            Title = "黑猫呕吐",
            Content = "今天黑猫吐了一次，精神还可以。",
            StructuredData = new Dictionary<string, object?>
            {
                ["tags"] = new[] { "猫", "健康" },
                ["catName"] = "黑猫",
                ["importance"] = 2,
                ["rawExtractedHints"] = "精神还可以"
            }
        };
    }

    private sealed class FakeAgentLifeEventWriter : IAgentLifeEventWriter
    {
        public string? LastPath { get; private set; }
        public LifeEvent? LastEvent { get; private set; }
        public Exception? ErrorToThrow { get; set; }

        public Task WriteAsync(
            string authenticatedUserId,
            string eventId,
            LifeEvent lifeEvent,
            CancellationToken cancellationToken = default)
        {
            LastPath = $"users/{authenticatedUserId}/life_events/{eventId}";
            LastEvent = lifeEvent;
            if (ErrorToThrow != null)
            {
                throw ErrorToThrow;
            }

            return Task.CompletedTask;
        }
    }
}
