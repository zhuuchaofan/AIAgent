using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class LifeEventService : ILifeEventService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<LifeEventService> _logger;

    public LifeEventService(FirestoreDb db, ILogger<LifeEventService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LifeEvent> SaveEventAsync(string userId, LifeEvent lifeEvent)
    {
        // ── 安全边界：强制覆盖系统元数据，绝对不信任调用方传入的值 ────────
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空，必须来自经过验签的认证上下文", nameof(userId));

        // 生成唯一 eventId（格式：evt_{32位hex}）
        var eventId = $"evt_{Guid.NewGuid():N}";

        var now = DateTime.UtcNow;

        lifeEvent.Id          = eventId;       // 由后端生成
        lifeEvent.UserId      = userId;        // 来自 FirebaseAuthMiddleware，不可被外部覆盖
        lifeEvent.CreatedAt   = now;           // 服务器时间
        lifeEvent.OccurredAt  = now;           // Phase 1：默认等于 CreatedAt，不解析自然语言时间
        lifeEvent.Source      = "manual";      // Phase 1 全部为手动录入

        // ── Firestore 写入路径：users/{userId}/life_events/{eventId} ───────
        var docRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events")
            .Document(eventId);

        _logger.LogInformation(
            "写入 Firestore: users/{UserId}/life_events/{EventId}（type={Type}）",
            userId, eventId, lifeEvent.Type);

        await docRef.SetAsync(lifeEvent);

        _logger.LogInformation("写入成功: {EventId}", eventId);
        return lifeEvent;
    }
}
