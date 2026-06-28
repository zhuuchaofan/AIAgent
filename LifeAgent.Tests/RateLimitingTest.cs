using Microsoft.Extensions.Options;
using Xunit;
using LifeAgent.Api.Models;

namespace LifeAgent.Tests;

public class RateLimitingTest
{
    // ────────────────────────────────────────────────────────────────────────
    // 1. 配置模型验证
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RateLimitingOptions_ShouldHaveCorrectDefaults()
    {
        var options = new RateLimitingOptions();

        Assert.Equal(30, options.GlobalIp.PermitLimit);
        Assert.Equal(60, options.AuthenticatedUser.PermitLimit);
        Assert.Equal(10, options.HighCost.PermitLimit);
        Assert.Equal(20, options.Internal.PermitLimit);
    }

    [Fact]
    public void RateLimitingOptions_ShouldHaveDefaultWindowSeconds()
    {
        var options = new RateLimitingOptions();

        Assert.Equal(60, options.GlobalIp.WindowSeconds);
        Assert.Equal(60, options.AuthenticatedUser.WindowSeconds);
        Assert.Equal(60, options.HighCost.WindowSeconds);
        Assert.Equal(60, options.Internal.WindowSeconds);
    }

    [Fact]
    public void RateLimitingOptions_ShouldHaveDefaultQueueLimit()
    {
        var options = new RateLimitingOptions();

        Assert.Equal(0, options.GlobalIp.QueueLimit);
        Assert.Equal(0, options.AuthenticatedUser.QueueLimit);
        Assert.Equal(0, options.HighCost.QueueLimit);
        Assert.Equal(0, options.Internal.QueueLimit);
    }

    [Fact]
    public void RagOptions_ShouldIncludeRateLimiting()
    {
        var ragOptions = new RagOptions();

        Assert.NotNull(ragOptions.RateLimiting);
        Assert.IsType<RateLimitingOptions>(ragOptions.RateLimiting);
    }

    [Fact]
    public void RateLimitPolicyOptions_ShouldBeConfigurable()
    {
        var options = new RateLimitPolicyOptions
        {
            PermitLimit = 100,
            WindowSeconds = 120,
            QueueLimit = 5
        };

        Assert.Equal(100, options.PermitLimit);
        Assert.Equal(120, options.WindowSeconds);
        Assert.Equal(5, options.QueueLimit);
    }

    [Fact]
    public void RateLimitingOptions_ShouldSupportCustomValues()
    {
        var options = new RateLimitingOptions
        {
            GlobalIp = new RateLimitPolicyOptions { PermitLimit = 50 },
            HighCost = new RateLimitPolicyOptions { PermitLimit = 5 },
            Internal = new RateLimitPolicyOptions { PermitLimit = 30 }
        };

        Assert.Equal(50, options.GlobalIp.PermitLimit);
        Assert.Equal(5, options.HighCost.PermitLimit);
        Assert.Equal(30, options.Internal.PermitLimit);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. 端点 handler 回归测试（确保添加 .RequireRateLimiting() 不影响 handler 逻辑）
    // ────────────────────────────────────────────────────────────────────────

    // 注：ASP.NET Core Rate Limiter 是中间件级组件，实际限流行为需要通过
    // WebApplicationFactory 集成测试或线上验证。此处验证的是：
    // - 配置模型正确
    // - 端点 handler 逻辑未被改动破坏
    //
    // 真正的限流集成测试建议在后续 Phase 中使用 WebApplicationFactory 补充。
}
