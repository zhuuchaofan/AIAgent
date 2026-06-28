namespace LifeAgent.Api.Services;

/// <summary>
/// 每日 API 调用配额服务，用于限制 Gemini LLM / Embedding 的调用量，防止费用失控。
/// </summary>
public interface IDailyQuotaService
{
    /// <summary>
    /// 检查指定用户是否还有当日配额，若有则递增计数并返回 true，否则返回 false。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="quotaType">配额类型（如 "llm"、"embedding"、"document"）</param>
    /// <returns>true = 允许调用，false = 配额已用尽</returns>
    bool CheckAndIncrement(string userId, string quotaType);

    /// <summary>
    /// 获取指定用户某类配额的当日剩余次数。
    /// </summary>
    int GetRemaining(string userId, string quotaType);

    /// <summary>
    /// 获取指定用户某类配额的每日上限。
    /// </summary>
    int GetDailyLimit(string quotaType);
}
