using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Xunit;

namespace LifeAgent.Tests;

/// <summary>
/// 长期记忆校验器 (MemoryValidator) 单元测试。
/// 验证 11 项校验规则，包含空内容拦截、时有时限校验、安全红线拦截以及 metadata 大小限制。
/// </summary>
public class MemoryValidatorTest
{
    private Memory CreateValidMockMemory()
    {
        return new Memory
        {
            Id = "mem_12345",
            UserId = "user_a",
            Type = "preference",
            Status = "active",
            Content = "我喜欢在早晨喝燕麦拿铁咖啡。",
            Confidence = 0.95,
            Importance = 3,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public void Validator_Passes_ValidMemory()
    {
        var memory = CreateValidMockMemory();
        // 验证合法的记忆实例能成功通过校验不抛出异常
        MemoryValidator.Validate(memory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_Rejects_InvalidUserId(string? invalidUserId)
    {
        var memory = CreateValidMockMemory();
        memory.UserId = invalidUserId!;

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("UserId", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid_type")]
    public void Validator_Rejects_InvalidType(string? invalidType)
    {
        var memory = CreateValidMockMemory();
        memory.Type = invalidType!;

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("Type", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid_status")]
    public void Validator_Rejects_InvalidStatus(string? invalidStatus)
    {
        var memory = CreateValidMockMemory();
        memory.Status = invalidStatus!;

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("Status", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_Rejects_EmptyContent(string? emptyContent)
    {
        var memory = CreateValidMockMemory();
        memory.Content = emptyContent!;

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("Content", exception.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void Validator_Rejects_InvalidConfidence(double invalidConfidence)
    {
        var memory = CreateValidMockMemory();
        memory.Confidence = invalidConfidence;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("Confidence", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Validator_Rejects_InvalidImportance(int invalidImportance)
    {
        var memory = CreateValidMockMemory();
        memory.Importance = invalidImportance;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("Importance", exception.Message);
    }

    [Fact]
    public void Validator_TemporaryContext_RequiresExpiresAt()
    {
        var memory = CreateValidMockMemory();
        memory.Type = "temporary_context";
        memory.ExpiresAt = null; // 无过期时间

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("ExpiresAt", exception.Message);
    }

    [Fact]
    public void Validator_TemporaryContext_AcceptsValidFutureExpiresAt()
    {
        var memory = CreateValidMockMemory();
        memory.Type = "temporary_context";
        memory.ExpiresAt = DateTime.UtcNow.AddDays(2); // 合法的未来时间

        MemoryValidator.Validate(memory);
    }

    [Fact]
    public void Validator_ConstraintType_RequiresImportanceLevelFive()
    {
        var memory = CreateValidMockMemory();
        memory.Type = "constraint";
        memory.Importance = 3; // 红线类型，重要性却为3

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("constraint", exception.Message);
        Assert.Contains("Importance", exception.Message);

        memory.Importance = 5; // 改成最高级5
        MemoryValidator.Validate(memory);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("my_token_id")]
    [InlineData("GEMINI_API_KEY")]
    [InlineData("clientSecret")]
    [InlineData("authorization")]
    [InlineData("credential_details")]
    public void Validator_Rejects_SensitiveMetadataKeys(string forbiddenKey)
    {
        var memory = CreateValidMockMemory();
        memory.Metadata = new Dictionary<string, object>
        {
            { forbiddenKey, "some_value" }
        };

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("敏感字段", exception.Message);
    }

    [Fact]
    public void Validator_Rejects_MetadataContainingRawPayload()
    {
        var memory = CreateValidMockMemory();

        // 模拟超长 raw payload 的元数据输入
        var largePayload = new string('A', 2048);
        memory.Metadata = new Dictionary<string, object>
        {
            { "payload", largePayload }
        };

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("raw payload", exception.Message);
    }

    /// <summary>
    /// 【Phase 6.1 安全保守策略测试】
    /// 验证若记忆 Content 中混入了凭证等敏感大明文时触发拦截。
    /// 注意：此检查属于本开发阶段采用的正则表达式粗筛保守安全红线策略，不代表最终精细化 NLP 敏感信息检测方案。
    /// </summary>
    [Theory]
    [InlineData("我的 token 是 eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJncm91cCI6ImFkbWluIn0.abcde")]
    [InlineData("Authorization: Bearer mock_local_token_123")]
    public void Validator_Rejects_ContentWithSuspiciousTokens_AsConservativeStrategy(string suspiciousContent)
    {
        var memory = CreateValidMockMemory();
        memory.Content = suspiciousContent;

        var exception = Assert.Throws<ArgumentException>(() => MemoryValidator.Validate(memory));
        Assert.Contains("疑似 JWT Token 或 Bearer", exception.Message);
    }
}
