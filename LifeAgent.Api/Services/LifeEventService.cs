using System.Text;
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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SaveEventAsync
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <inheritdoc/>
    public async Task<LifeEvent> SaveEventAsync(string userId, LifeEvent lifeEvent)
    {
        // ── 安全边界：强制覆盖系统元数据，绝对不信任调用方传入的值 ────────
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空，必须来自经过验签的认证上下文", nameof(userId));

        // 生成唯一 eventId（格式：evt_{32位hex}）
        var eventId = $"evt_{Guid.NewGuid():N}";

        var now = DateTime.UtcNow;

        lifeEvent.Id         = eventId;   // 由后端生成
        lifeEvent.UserId     = userId;    // 来自 FirebaseAuthMiddleware，不可被外部覆盖
        lifeEvent.CreatedAt  = now;       // 服务器时间
        lifeEvent.OccurredAt = now;       // Phase 1：默认等于 CreatedAt，不解析自然语言时间
        lifeEvent.Source     = "manual";  // Phase 1 全部为手动录入

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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ListEventsAsync
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <inheritdoc/>
    public async Task<ListEventsResult> ListEventsAsync(
        string userId,
        string? type   = null,
        int     limit  = 20,
        string? cursor = null)
    {
        // ── 参数校验 ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        // 上限 100，下限 1
        limit = Math.Clamp(limit, 1, 100);

        // ── 构建基础查询 ───────────────────────────────────────────────
        // 路径：users/{userId}/life_events
        // 排序：occurredAt DESC → DocumentId DESC（稳定排序，tie-break）
        var collection = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events");

        Query query = collection
            .OrderByDescending("occurredAt")
            .OrderByDescending(FieldPath.DocumentId);

        // ── 可选 type 过滤 ────────────────────────────────────────────
        // 注意：type + occurredAt 排序需要 Firestore 复合索引
        // 如未建索引，Firestore 会返回 FAILED_PRECONDITION 并附上 Console 建索引链接
        if (!string.IsNullOrEmpty(type)
            && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.WhereEqualTo("type", type);
        }

        // ── Cursor 解码 ────────────────────────────────────────────────
        // 格式：Base64(UTF-8, "occurredAt_RFC3339|documentId")
        // 示例：Base64("2026-06-24T13:01:04.7212240Z|evt_8733dcd7...")
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var sep     = decoded.IndexOf('|');
                if (sep < 0)
                    throw new FormatException("cursor 格式不合法：缺少 '|' 分隔符");

                var occurredAtStr = decoded[..sep];
                var lastDocId     = decoded[(sep + 1)..];

                var lastOccurredAt = DateTime.Parse(
                    occurredAtStr,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind);

                // StartAfter(occurredAt, DocumentReference) 对应两个 OrderBy 字段
                var lastDocRef = collection.Document(lastDocId);
                query = query.StartAfter(Timestamp.FromDateTime(lastOccurredAt), lastDocRef);

                _logger.LogDebug(
                    "使用 cursor 翻页：lastOccurredAt={OccurredAt}, lastDocId={DocId}",
                    occurredAtStr, lastDocId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "cursor 解码失败，忽略 cursor 从第一页查询");
                // 容错：cursor 损坏时降级为第一页，不返回 400
            }
        }

        // ── 执行查询（多取 1 条，用于判断是否有下一页）───────────────────
        // 不使用 offset，纯 cursor 分页
        query = query.Limit(limit + 1);

        var snapshot = await query.GetSnapshotAsync();
        var docs     = snapshot.Documents;

        bool hasMore = docs.Count > limit;

        // 截取本页数据（最多 limit 条）
        var pageItems = docs
            .Take(limit)
            .Select(d => d.ConvertTo<LifeEvent>())
            .ToList();

        // ── Cursor 编码 ────────────────────────────────────────────────
        string? nextCursor = null;
        if (hasMore && pageItems.Count > 0)
        {
            var last       = pageItems[^1];
            var raw        = $"{last.OccurredAt:O}|{last.Id}";
            nextCursor     = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

            _logger.LogDebug(
                "生成 nextCursor：raw={Raw}", raw);
        }

        _logger.LogInformation(
            "ListEventsAsync: userId={UserId}, type={Type}, limit={Limit}, 返回={Count}, hasMore={HasMore}",
            userId, type ?? "all", limit, pageItems.Count, hasMore);

        return new ListEventsResult
        {
            Data       = pageItems,
            NextCursor = nextCursor
        };
    }

    public async Task<LifeEvent?> GetEventAsync(string userId, string eventId)
    {
        var docRef = _db.Collection("users").Document(userId).Collection("life_events").Document(eventId);
        var snapshot = await docRef.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            return null;
        }

        return snapshot.ConvertTo<LifeEvent>();
    }
}
