using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

/// <summary>
/// 基于内存的每日配额计数器（线程安全，UTC 每日自动重置）。
/// 适用于 Cloud Run 单实例部署场景；如需多实例共享，未来可迁移到 Firestore 或 Redis。
/// </summary>
public class DailyQuotaService : IDailyQuotaService
{
    private readonly RagOptions _options;

    // Key: $"{quotaType}:{userId}:{utcDate}" → 已调用次数
    private readonly ConcurrentDictionary<string, int> _counters = new();

    // 配额类型 → 每日上限的映射键
    public const string QuotaTypeLlm = "llm";
    public const string QuotaTypeEmbedding = "embedding";
    public const string QuotaTypeDocument = "document";

    public DailyQuotaService(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public bool CheckAndIncrement(string userId, string quotaType)
    {
        var limit = GetDailyLimit(quotaType);
        if (limit <= 0) return true; // 限制为 0 或负数表示不限制

        var key = BuildKey(userId, quotaType);
        var newValue = _counters.AddOrUpdate(key, 1, (_, old) => old + 1);
        return newValue <= limit;
    }

    public int GetRemaining(string userId, string quotaType)
    {
        var limit = GetDailyLimit(quotaType);
        if (limit <= 0) return int.MaxValue;

        var key = BuildKey(userId, quotaType);
        _counters.TryGetValue(key, out var used);
        return Math.Max(0, limit - used);
    }

    public int GetDailyLimit(string quotaType)
    {
        return quotaType switch
        {
            QuotaTypeLlm => _options.DailyLlmCallLimit,
            QuotaTypeEmbedding => _options.DailyEmbeddingCallLimit,
            QuotaTypeDocument => _options.DailyDocumentProcessLimit,
            _ => 0
        };
    }

    /// <summary>
    /// 构建计数器 Key，包含 UTC 日期以实现每日自动重置。
    /// 隔天后 Key 变化，旧计数自然失效（ConcurrentDictionary 中残留但不影响逻辑）。
    /// </summary>
    private static string BuildKey(string userId, string quotaType)
    {
        return $"{quotaType}:{userId}:{DateTime.UtcNow:yyyyMMdd}";
    }
}
