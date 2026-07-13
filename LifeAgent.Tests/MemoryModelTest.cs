using LifeAgent.Api.Models.Memories;
using Xunit;

namespace LifeAgent.Tests;

/// <summary>
/// 验证长期记忆数据模型、MemoryType 枚举与字符串转换的正确性。
/// </summary>
public class MemoryModelTest
{
    [Fact]
    public void Memory_CanBeInstantiatedWithDefaultValues()
    {
        var memory = new Memory();

        Assert.Equal(string.Empty, memory.Id);
        Assert.Equal(string.Empty, memory.UserId);
        Assert.Equal(string.Empty, memory.Type);
        Assert.Equal(string.Empty, memory.Status);
        Assert.Equal(string.Empty, memory.Content);
        Assert.Equal(1.0, memory.Confidence);
        Assert.Equal(3, memory.Importance);
        Assert.Equal("manual_entry", memory.Source);
        Assert.Null(memory.AgentActionId);
        Assert.Null(memory.ExpiresAt);
        Assert.Null(memory.Metadata);
        Assert.Equal(0, memory.RecCount);
        Assert.Null(memory.LastRecalledAt);
    }

    [Fact]
    public void MemoryTypeHelper_CorrectlyMapsAllEnumTypes()
    {
        var allEnumTypes = Enum.GetValues<MemoryType>();
        Assert.Equal(13, allEnumTypes.Length);

        foreach (var type in allEnumTypes)
        {
            var snakeStr = type.ToSnakeCaseString();
            Assert.False(string.IsNullOrWhiteSpace(snakeStr));

            // 验证 Helper 可还原
            var mappedEnum = MemoryTypeHelper.FromString(snakeStr);
            Assert.NotNull(mappedEnum);
            Assert.Equal(type, mappedEnum.Value);

            // 验证 IsValid 返回 true
            Assert.True(MemoryTypeHelper.IsValid(snakeStr));
        }
    }

    [Theory]
    [InlineData("life_event", MemoryType.LifeEvent)]
    [InlineData("preference", MemoryType.Preference)]
    [InlineData("goal", MemoryType.Goal)]
    [InlineData("habit", MemoryType.Habit)]
    [InlineData("relationship", MemoryType.Relationship)]
    [InlineData("knowledge", MemoryType.Knowledge)]
    [InlineData("project", MemoryType.Project)]
    [InlineData("person", MemoryType.Person)]
    [InlineData("location", MemoryType.Location)]
    [InlineData("routine", MemoryType.Routine)]
    [InlineData("constraint", MemoryType.Constraint)]
    [InlineData("temporary_context", MemoryType.TemporaryContext)]
    [InlineData("theme", MemoryType.Theme)]
    public void MemoryTypeHelper_MapsSpecificStringsToCorrectEnums(string inputString, MemoryType expectedType)
    {
        Assert.True(MemoryTypeHelper.IsValid(inputString));
        var mapped = MemoryTypeHelper.FromString(inputString);
        Assert.Equal(expectedType, mapped);
        Assert.Equal(inputString, expectedType.ToSnakeCaseString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown_type")]
    [InlineData("life-event")] // 用了中划线，应返回无效
    public void MemoryTypeHelper_RejectsInvalidStrings(string invalidString)
    {
        Assert.False(MemoryTypeHelper.IsValid(invalidString));
        Assert.Null(MemoryTypeHelper.FromString(invalidString));
    }
}
