using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.LifeEvents;
using Microsoft.Extensions.Configuration;

namespace LifeAgent.Tests;

public class AgentLifeEventSkeletonTest
{
    [Fact]
    public async Task CreateFromAgentConfirmation_GeneratesSystemFields()
    {
        var service = new InMemoryAgentLifeEventService();
        var request = ValidRequest();

        var lifeEvent = await service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", request);

        Assert.StartsWith("evt_", lifeEvent.Id);
        Assert.Equal("auth_user", lifeEvent.UserId);
        Assert.Equal("agent_confirmed", lifeEvent.Source);
        Assert.Equal("agent", lifeEvent.CreatedBy);
        Assert.Equal("agent_action_1", lifeEvent.AgentActionId);
        Assert.True(lifeEvent.CreatedAt > DateTime.MinValue);
        Assert.True(lifeEvent.UpdatedAt >= lifeEvent.CreatedAt);
        Assert.DoesNotContain("agentActionId", lifeEvent.StructuredData.Keys);
        Assert.DoesNotContain("createdBy", lifeEvent.StructuredData.Keys);
        Assert.Single(service.Events);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_UsesAuthenticatedUserIdAndRejectsPayloadUserId()
    {
        var service = new InMemoryAgentLifeEventService();
        var request = ValidRequest();
        request.UserId = "payload_user";

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", request));

        Assert.Contains("system fields", error.Message);
        Assert.Empty(service.Events);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("source")]
    [InlineData("createdBy")]
    [InlineData("agentActionId")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("token")]
    [InlineData("secret")]
    [InlineData("internalPath")]
    [InlineData("firestorePath")]
    public void Validator_RejectsForbiddenStructuredDataFields(string key)
    {
        var request = ValidRequest();
        request.StructuredData[key] = "bad";

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventPayloadValidator.ValidateAndSanitize(request));

        Assert.Contains("forbidden", error.Message);
    }

    [Fact]
    public void Validator_RejectsNestedForbiddenStructuredDataFields()
    {
        var request = ValidRequest();
        request.StructuredData["rawExtractedHints"] = new Dictionary<string, object?>
        {
            ["token"] = "bad"
        };

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventPayloadValidator.ValidateAndSanitize(request));

        Assert.Contains("forbidden", error.Message);
    }

    [Fact]
    public void Validator_AllowsOnlyWhitelistedStructuredDataFields()
    {
        var request = ValidRequest();
        request.StructuredData["unknownField"] = "bad";

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventPayloadValidator.ValidateAndSanitize(request));

        Assert.Contains("not allowed", error.Message);
    }

    [Fact]
    public void Validator_PreservesAllowedStructuredDataFields()
    {
        var request = ValidRequest();

        var sanitized = LifeEventPayloadValidator.ValidateAndSanitize(request);

        Assert.Equal("黑猫", sanitized["catName"]);
        Assert.Equal("calm", sanitized["mood"]);
        Assert.Equal(2, sanitized["importance"]);
        Assert.Equal("客厅", sanitized["locationLabel"]);
        Assert.Equal("观察精神状态", sanitized["rawExtractedHints"]);
        Assert.Equal(new[] { "猫", "健康" }, Assert.IsType<string[]>(sanitized["tags"]));
    }

    [Theory]
    [InlineData("type", 51)]
    [InlineData("title", 121)]
    [InlineData("content", 2001)]
    public void Validator_RejectsOverlongCoreFields(string fieldName, int length)
    {
        var request = ValidRequest();
        var value = new string('a', length);
        if (fieldName == "type")
        {
            request.Type = value;
        }
        else if (fieldName == "title")
        {
            request.Title = value;
        }
        else
        {
            request.Content = value;
        }

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventPayloadValidator.ValidateAndSanitize(request));

        Assert.Contains("exceeds max length", error.Message);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("title")]
    [InlineData("content")]
    public void Validator_RejectsMissingCoreFields(string fieldName)
    {
        var request = ValidRequest();
        if (fieldName == "type")
        {
            request.Type = "";
        }
        else if (fieldName == "title")
        {
            request.Title = "";
        }
        else
        {
            request.Content = "";
        }

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventPayloadValidator.ValidateAndSanitize(request));

        Assert.Contains("required", error.Message);
    }

    [Fact]
    public async Task CreateFromAgentConfirmation_DoesNotUsePayloadSystemFields()
    {
        var service = new InMemoryAgentLifeEventService();
        var request = ValidRequest();
        request.Id = "evt_payload";
        request.Source = "manual";
        request.CreatedBy = "user";
        request.AgentActionId = "payload_action";
        request.CreatedAt = DateTime.UtcNow.AddDays(-1);
        request.UpdatedAt = DateTime.UtcNow.AddDays(-1);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateFromAgentConfirmationAsync("auth_user", "agent_action_1", request));

        Assert.Contains("system fields", error.Message);
        Assert.Empty(service.Events);
    }

    [Fact]
    public void AgentWriteFeatureOptions_DefaultsToDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = AgentWriteFeatureOptions.FromConfiguration(configuration);

        Assert.False(options.EnableAgentWriteTools);
        Assert.False(options.EnableCreateLifeEventTool);
        Assert.False(options.CanCreateLifeEvent);
    }

    [Fact]
    public void AgentWriteFeatureOptions_RequiresBothFlags()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AgentWriteFeatureOptions.EnableAgentWriteToolsEnvName] = "true",
                [AgentWriteFeatureOptions.EnableCreateLifeEventToolEnvName] = "false"
            })
            .Build();

        var options = AgentWriteFeatureOptions.FromConfiguration(configuration);

        Assert.True(options.EnableAgentWriteTools);
        Assert.False(options.EnableCreateLifeEventTool);
        Assert.False(options.CanCreateLifeEvent);
    }

    private static CreateLifeEventRequest ValidRequest()
    {
        return new CreateLifeEventRequest
        {
            Type = "cat",
            Title = "观察黑猫状态",
            Content = "明天观察黑猫的精神和食欲。",
            StructuredData = new Dictionary<string, object?>
            {
                ["tags"] = new[] { "猫", "健康" },
                ["catName"] = "黑猫",
                ["mood"] = "calm",
                ["importance"] = 2,
                ["locationLabel"] = "客厅",
                ["rawExtractedHints"] = "观察精神状态"
            }
        };
    }
}
