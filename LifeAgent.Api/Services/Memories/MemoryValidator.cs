using System.Text.Json;
using System.Text.RegularExpressions;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

/// <summary>
/// 长期记忆校验器。实现对长期记忆实体的 Taxonomy、Schema 校验以及数据隐私敏感词红线拦截。
/// </summary>
public static class MemoryValidator
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "apiKey", "secret", "authorization", "credential"
    };

    // 保守安全策略：识别典型的凭证泄露特征（如 JWT token 结构或 Bearer 认证串）
    private static readonly Regex JwtRegex = new(@"eyJ[a-zA-Z0-9-_=]+\.[a-zA-Z0-9-_=]+\.?[a-zA-Z0-9-_=]*", RegexOptions.Compiled);
    private static readonly Regex BearerRegex = new(@"bearer\s+[a-zA-Z0-9-_=\.]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 校验记忆实体是否符合规范。若校验失败则抛出 ArgumentException。
    /// </summary>
    public static void Validate(Memory memory)
    {
        if (memory == null)
            throw new ArgumentNullException(nameof(memory));

        // 1. userId 必填
        if (string.IsNullOrWhiteSpace(memory.UserId))
            throw new ArgumentException("UserId 必填，不可为空。", nameof(memory));

        // 2. type 必须在 MemoryType 内
        if (!MemoryTypeHelper.IsValid(memory.Type))
            throw new ArgumentException($"无效的记忆类型 (Type): '{memory.Type}'。请参考 MemoryType 定义。", nameof(memory));

        // 3. status 必须在 MemoryStatus 内
        if (!MemoryStatusHelper.IsValid(memory.Status))
            throw new ArgumentException($"无效的记忆状态 (Status): '{memory.Status}'。请参考 MemoryStatus 定义。", nameof(memory));

        // 4. content 必填且不能只有空白
        if (string.IsNullOrWhiteSpace(memory.Content))
            throw new ArgumentException("Memory Content 必填且不能为空白字符。", nameof(memory));

        // 5. confidence 范围 0 到 1
        if (memory.Confidence < 0.0 || memory.Confidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(memory), "Confidence 必须处于 [0.0, 1.0] 的区间内。");

        // 6. importance 评分系统设定在 1 到 5 之间
        if (memory.Importance < 1 || memory.Importance > 5)
            throw new ArgumentOutOfRangeException(nameof(memory), "Importance 重要度必须在 [1, 5] 区间内。");

        var enumType = MemoryTypeHelper.FromString(memory.Type);

        // 7. temporary_context 必须有 expiresAt
        if (enumType == MemoryType.TemporaryContext)
        {
            if (!memory.ExpiresAt.HasValue)
            {
                throw new ArgumentException("当记忆类型为 'temporary_context' 时，ExpiresAt 过期时间为必填项。", nameof(memory));
            }
            if (memory.ExpiresAt.Value <= DateTime.UtcNow)
            {
                throw new ArgumentException("temporary_context 的 ExpiresAt 必须是未来的时间。", nameof(memory));
            }
        }
        else
        {
            // 8. 非 temporary_context 默认不要求 expiresAt
            // 保守策略下，如果设置了，应警告或清除，当前保持原样但作日志。
        }

        // 9. constraint 类型默认 high importance (最高级别 5)
        if (enumType == MemoryType.Constraint)
        {
            if (memory.Importance != 5)
            {
                throw new ArgumentException("当记忆类型为 'constraint' 红线约束时，其 Importance 重要度级别必须默认为最高级别 5。", nameof(memory));
            }
        }

        // 10. 敏感字段过滤（Metadata key 的敏感字段拦截）
        if (memory.Metadata != null)
        {
            // 校验 metadata 的 key，绝对不允许包含任何隐私敏感字词
            foreach (var key in memory.Metadata.Keys)
            {
                // 标准化键名：去除下划线与中划线以防类似 api_key 绕过
                var normalizedKey = key.Replace("_", "").Replace("-", "");
                if (SensitiveKeys.Any(s => normalizedKey.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"Metadata 的键名中包含被禁止的敏感字段: '{key}'。", nameof(memory));
                }
            }

            // 11. 不允许把完整 raw payload 当作 metadata 存储
            // 检查 metadata value 的长度与结构特征，限制总长度，杜绝 raw payload 直接倾倒
            var serializedMetadata = JsonSerializer.Serialize(memory.Metadata);
            if (serializedMetadata.Length > 2000)
            {
                throw new ArgumentException("Metadata 数据包总字符数超过了 2000 字节，疑似塞入了完整的原始 raw payload，已被安全拦截。", nameof(memory));
            }

            if (memory.Metadata.Keys.Any(k => string.Equals(k, "payload", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(k, "raw", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Metadata 中严禁包含名为 'payload' 或 'raw' 的大对象键名，以防塞入原始文本。", nameof(memory));
            }
        }

        // 10. (补齐) 针对 Content 的保守安全红线策略：防止明文凭证直接沉淀为记忆
        if (JwtRegex.IsMatch(memory.Content) || BearerRegex.IsMatch(memory.Content))
        {
            throw new ArgumentException("检测到 Memory Content 中包含疑似 JWT Token 或 Bearer 身份凭证信息，触发安全红线拦截。此为 Phase 6.1 保守策略。", nameof(memory));
        }
    }
}
