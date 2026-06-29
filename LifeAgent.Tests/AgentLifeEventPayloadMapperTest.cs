using System.Text.Json;
using LifeAgent.Api.Services.LifeEvents;

namespace LifeAgent.Tests;

public class AgentLifeEventPayloadMapperTest
{
    [Fact]
    public void Map_ValidPayload_ReturnsCreateLifeEventRequest()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            type = "cat_health",
            title = "黑猫呕吐",
            content = "今天黑猫吐了一次，精神还可以。",
            structuredData = new
            {
                tags = new[] { "猫", "健康" },
                catName = "黑猫",
                mood = "normal",
                importance = 2,
                locationLabel = "家里",
                rawExtractedHints = "精神还可以"
            }
        });

        var request = LifeEventActionPayloadMapper.Map(payload);

        Assert.Equal("cat_health", request.Type);
        Assert.Equal("黑猫呕吐", request.Title);
        Assert.Equal("今天黑猫吐了一次，精神还可以。", request.Content);
        Assert.Equal("黑猫", request.StructuredData["catName"]);
        Assert.Equal("normal", request.StructuredData["mood"]);
        Assert.Equal(2L, request.StructuredData["importance"]);
        Assert.Equal("家里", request.StructuredData["locationLabel"]);
        Assert.Equal("精神还可以", request.StructuredData["rawExtractedHints"]);
        Assert.Equal(new List<object> { "猫", "健康" }, Assert.IsType<List<object>>(request.StructuredData["tags"]));
        Assert.Null(request.UserId);
        Assert.Null(request.Id);
        Assert.Null(request.Source);
        Assert.Null(request.CreatedBy);
        Assert.Null(request.AgentActionId);
        Assert.Null(request.CreatedAt);
        Assert.Null(request.UpdatedAt);
    }

    [Theory]
    [InlineData("userId")]
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
    public void Map_TopLevelForbiddenField_FailsClosed(string fieldName)
    {
        var json = $$"""
        {
          "type": "cat",
          "title": "黑猫呕吐",
          "content": "今天黑猫吐了一次",
          "{{fieldName}}": "bad"
        }
        """;

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(json));

        Assert.Contains("forbidden", error.Message);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("secret")]
    [InlineData("internalPath")]
    [InlineData("firestorePath")]
    [InlineData("userId")]
    public void Map_StructuredDataForbiddenField_FailsClosed(string fieldName)
    {
        var json = $$"""
        {
          "type": "cat",
          "title": "黑猫呕吐",
          "content": "今天黑猫吐了一次",
          "structuredData": {
            "{{fieldName}}": "bad"
          }
        }
        """;

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(json));

        Assert.Contains("forbidden", error.Message);
    }

    [Fact]
    public void Map_NestedStructuredDataForbiddenField_FailsClosed()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            type = "cat",
            title = "黑猫呕吐",
            content = "今天黑猫吐了一次",
            structuredData = new
            {
                rawExtractedHints = new
                {
                    token = "bad"
                }
            }
        });

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(payload));

        Assert.Contains("forbidden", error.Message);
    }

    [Fact]
    public void Map_UnknownTopLevelField_FailsClosed()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            type = "cat",
            title = "黑猫呕吐",
            content = "今天黑猫吐了一次",
            unexpected = "bad"
        });

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(payload));

        Assert.Contains("not allowed", error.Message);
    }

    [Fact]
    public void Map_UnknownStructuredDataField_FailsClosed()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            type = "cat",
            title = "黑猫呕吐",
            content = "今天黑猫吐了一次",
            structuredData = new
            {
                unknown = "bad"
            }
        });

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(payload));

        Assert.Contains("not allowed", error.Message);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("title")]
    [InlineData("content")]
    public void Map_MissingRequiredCoreField_FailsValidation(string fieldName)
    {
        var fields = new Dictionary<string, object?>
        {
            ["type"] = "cat",
            ["title"] = "黑猫呕吐",
            ["content"] = "今天黑猫吐了一次"
        };
        fields.Remove(fieldName);

        var json = JsonSerializer.Serialize(fields);

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(json));

        Assert.Contains("required", error.Message);
    }

    [Theory]
    [InlineData("type", 51)]
    [InlineData("title", 121)]
    [InlineData("content", 2001)]
    public void Map_OverlongCoreField_FailsValidation(string fieldName, int length)
    {
        var fields = new Dictionary<string, object?>
        {
            ["type"] = "cat",
            ["title"] = "黑猫呕吐",
            ["content"] = "今天黑猫吐了一次"
        };
        fields[fieldName] = new string('a', length);

        var json = JsonSerializer.Serialize(fields);

        var error = Assert.Throws<ArgumentException>(() =>
            LifeEventActionPayloadMapper.Map(json));

        Assert.Contains("exceeds max length", error.Message);
    }

    [Fact]
    public void Map_DoesNotWriteToInMemoryService()
    {
        var service = new InMemoryAgentLifeEventService();
        var payload = JsonSerializer.SerializeToElement(new
        {
            type = "cat",
            title = "黑猫呕吐",
            content = "今天黑猫吐了一次"
        });

        _ = LifeEventActionPayloadMapper.Map(payload);

        Assert.Empty(service.Events);
    }
}
