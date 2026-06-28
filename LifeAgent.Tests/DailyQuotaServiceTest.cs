using Microsoft.Extensions.Options;
using Xunit;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class DailyQuotaServiceTest
{
    private static DailyQuotaService CreateService(
        int llmLimit = 200, int embeddingLimit = 500, int documentLimit = 20)
    {
        var options = Options.Create(new RagOptions
        {
            DailyLlmCallLimit = llmLimit,
            DailyEmbeddingCallLimit = embeddingLimit,
            DailyDocumentProcessLimit = documentLimit
        });
        return new DailyQuotaService(options);
    }

    // ────────────────────────────────────────────────────────────────
    // 1. 基本功能：未超额时正常放行
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_BelowLimit_ShouldReturnTrue()
    {
        var service = CreateService(llmLimit: 5);
        var userId = "user_001";

        for (int i = 0; i < 5; i++)
        {
            Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));
        }
    }

    [Fact]
    public void GetRemaining_BelowLimit_ShouldDecrease()
    {
        var service = CreateService(llmLimit: 10);
        var userId = "user_001";

        Assert.Equal(10, service.GetRemaining(userId, DailyQuotaService.QuotaTypeLlm));

        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm);
        Assert.Equal(9, service.GetRemaining(userId, DailyQuotaService.QuotaTypeLlm));

        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm);
        Assert.Equal(8, service.GetRemaining(userId, DailyQuotaService.QuotaTypeLlm));
    }

    // ────────────────────────────────────────────────────────────────
    // 2. 超额行为：达到上限后返回 false
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_AtLimit_ShouldReturnFalse()
    {
        var service = CreateService(llmLimit: 3);
        var userId = "user_001";

        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));   // 1
        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));   // 2
        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));   // 3
        Assert.False(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));  // 4 → 超额
    }

    [Fact]
    public void GetRemaining_AtLimit_ShouldReturnZero()
    {
        var service = CreateService(embeddingLimit: 2);
        var userId = "user_001";

        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeEmbedding);
        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeEmbedding);

        Assert.Equal(0, service.GetRemaining(userId, DailyQuotaService.QuotaTypeEmbedding));
    }

    // ────────────────────────────────────────────────────────────────
    // 3. 用户隔离：不同 userId 的额度独立
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_DifferentUsers_ShouldBeIsolated()
    {
        var service = CreateService(llmLimit: 2);
        var userA = "user_A";
        var userB = "user_B";

        // User A 用完额度
        Assert.True(service.CheckAndIncrement(userA, DailyQuotaService.QuotaTypeLlm));
        Assert.True(service.CheckAndIncrement(userA, DailyQuotaService.QuotaTypeLlm));
        Assert.False(service.CheckAndIncrement(userA, DailyQuotaService.QuotaTypeLlm));

        // User B 仍有额度
        Assert.True(service.CheckAndIncrement(userB, DailyQuotaService.QuotaTypeLlm));
        Assert.Equal(1, service.GetRemaining(userB, DailyQuotaService.QuotaTypeLlm));
    }

    // ────────────────────────────────────────────────────────────────
    // 4. 类型隔离：不同 quotaType 的额度独立
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_DifferentQuotaTypes_ShouldBeIndependent()
    {
        var service = CreateService(llmLimit: 1, embeddingLimit: 1, documentLimit: 1);
        var userId = "user_001";

        // 用完 LLM 额度
        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));
        Assert.False(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));

        // Embedding 和 Document 仍有额度
        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeEmbedding));
        Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeDocument));
    }

    // ────────────────────────────────────────────────────────────────
    // 5. GetDailyLimit 正确返回配置值
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetDailyLimit_ShouldReturnConfiguredValues()
    {
        var service = CreateService(llmLimit: 100, embeddingLimit: 300, documentLimit: 10);

        Assert.Equal(100, service.GetDailyLimit(DailyQuotaService.QuotaTypeLlm));
        Assert.Equal(300, service.GetDailyLimit(DailyQuotaService.QuotaTypeEmbedding));
        Assert.Equal(10, service.GetDailyLimit(DailyQuotaService.QuotaTypeDocument));
    }

    [Fact]
    public void GetDailyLimit_UnknownType_ShouldReturnZero()
    {
        var service = CreateService();
        Assert.Equal(0, service.GetDailyLimit("unknown_type"));
    }

    // ────────────────────────────────────────────────────────────────
    // 6. limit ≤ 0 时不限制
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_ZeroLimit_ShouldAlwaysAllow()
    {
        var service = CreateService(llmLimit: 0);
        var userId = "user_001";

        // 不限制，反复调用均应返回 true
        for (int i = 0; i < 100; i++)
        {
            Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));
        }

        // GetRemaining 应返回 int.MaxValue
        Assert.Equal(int.MaxValue, service.GetRemaining(userId, DailyQuotaService.QuotaTypeLlm));
    }

    // ────────────────────────────────────────────────────────────────
    // 7. 配置缺失时使用 RagOptions 默认值（200/500/20）
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultRagOptions_ShouldHaveReasonableQuotaDefaults()
    {
        var options = new RagOptions();

        Assert.Equal(200, options.DailyLlmCallLimit);
        Assert.Equal(500, options.DailyEmbeddingCallLimit);
        Assert.Equal(20, options.DailyDocumentProcessLimit);
    }

    [Fact]
    public void CheckAndIncrement_WithDefaultOptions_ShouldRespectDefaults()
    {
        // 使用 RagOptions 的默认值（不传自定义参数）
        var options = Options.Create(new RagOptions());
        var service = new DailyQuotaService(options);
        var userId = "user_001";

        // 默认 LLM limit = 200，前 200 次应放行
        for (int i = 0; i < 200; i++)
        {
            Assert.True(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));
        }
        // 第 201 次应拒绝
        Assert.False(service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeLlm));
    }

    // ────────────────────────────────────────────────────────────────
    // 8. 边界：CheckAndIncrement 超额后计数器仍然递增（防止并发绕过）
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckAndIncrement_AfterExceed_ShouldStillIncrementCounter()
    {
        var service = CreateService(documentLimit: 2);
        var userId = "user_001";

        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeDocument); // 1
        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeDocument); // 2
        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeDocument); // 3 (超额，但计数器继续)
        service.CheckAndIncrement(userId, DailyQuotaService.QuotaTypeDocument); // 4

        // 剩余额度应为 0（不是负数）
        Assert.Equal(0, service.GetRemaining(userId, DailyQuotaService.QuotaTypeDocument));
    }
}
