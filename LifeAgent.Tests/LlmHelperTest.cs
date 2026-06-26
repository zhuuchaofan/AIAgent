using Xunit;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class LlmHelperTest
{
    [Theory]
    [InlineData("{\"title\":\"test\"}", "{\"title\":\"test\"}")]
    [InlineData("```json\n{\"title\":\"test\"}\n```", "{\"title\":\"test\"}")]
    [InlineData("```\n{\"title\":\"test\"}\n```", "{\"title\":\"test\"}")]
    [InlineData("Some raw text before\n```json\n{\"title\":\"test\"}\n```\nSome text after", "{\"title\":\"test\"}")]
    [InlineData("   \n {\"title\":\"test\"} \n  ", "{\"title\":\"test\"}")]
    [InlineData("No braces at all", "")]
    public void ExtractJsonObject_CleansAndExtractsCorrectly(string input, string expected)
    {
        // Act
        var result = LlmHelper.ExtractJsonObject(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeserializationTest()
    {
        string json = @"
      {
        ""type"": ""cat"",
        ""title"": ""给猫剪指甲"",
        ""content"": ""安排在明天下午3点给猫剪指甲并设置提醒。"",
        ""tags"": [""宠物"", ""猫"", ""日常""],
        ""importance"": 2,
        ""structuredData"": {
          ""activity"": ""剪指甲""
        },
        ""extractionConfidence"": 0.95,
        ""needsReview"": false,
        ""reminder"": {
          ""hasIntent"": true,
          ""title"": ""给猫剪指甲"",
          ""description"": ""记得明天下午3点给猫剪指甲"",
          ""dueAtIso8601"": ""2026-06-27T07:00:00Z"",
          ""parseStatus"": ""success""
        }
      }";

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var parsedEvent = System.Text.Json.JsonSerializer.Deserialize<ParsedEvent>(json, options);

        Assert.NotNull(parsedEvent);
        Assert.NotNull(parsedEvent.Reminder);
        Assert.True(parsedEvent.Reminder.HasIntent);
        Assert.Equal("给猫剪指甲", parsedEvent.Reminder.Title);
        Assert.Equal("2026-06-27T07:00:00Z", parsedEvent.Reminder.DueAtIso8601);
        Assert.Equal("success", parsedEvent.Reminder.ParseStatus);
    }
}
