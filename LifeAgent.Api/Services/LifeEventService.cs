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

    /// <inheritdoc/>
    public async Task<(LifeEvent, Reminder?)> SaveEventWithReminderAsync(string userId, LifeEvent lifeEvent, ParsedEvent parsedEvent)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        var eventId = $"evt_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        lifeEvent.Id         = eventId;
        lifeEvent.UserId     = userId;
        lifeEvent.CreatedAt  = now;
        lifeEvent.OccurredAt = now;
        lifeEvent.Source     = "manual";

        Reminder? reminder = null;

        if (parsedEvent.DetectedReminderIntent)
        {
            lifeEvent.ReminderIntentDetected = true;
            if (string.IsNullOrWhiteSpace(parsedEvent.ReminderDueAtIso))
            {
                lifeEvent.ReminderParseStatus = "missing_due_time";
                lifeEvent.ReminderParseNote = "检测到提醒意图但完全缺失时间";
            }
            else
            {
                if (DateTime.TryParse(parsedEvent.ReminderDueAtIso, out var parsedUtc))
                {
                    lifeEvent.ReminderParseStatus = "success";
                    var reminderId = $"rem_{Guid.NewGuid():N}";
                    lifeEvent.CreatedReminderId = reminderId;

                    reminder = new Reminder
                    {
                        Id = reminderId,
                        UserId = userId,
                        SourceEventId = eventId,
                        Title = parsedEvent.ReminderTitle ?? lifeEvent.Title,
                        Description = parsedEvent.ReminderDescription,
                        DueAt = parsedUtc.ToUniversalTime(),
                        Timezone = lifeEvent.TimeZone,
                        Status = "pending",
                        RepeatRule = "none",
                        CreatedAt = now,
                        UpdatedAt = now,
                        LlmConfidence = parsedEvent.ExtractionConfidence,
                        RawText = lifeEvent.Content
                    };
                }
                else
                {
                    lifeEvent.ReminderParseStatus = "invalid_due_time";
                    lifeEvent.ReminderParseNote = $"检测到提醒意图，但时间格式不合法: {parsedEvent.ReminderDueAtIso}";
                }
            }
        }
        else
        {
            lifeEvent.ReminderIntentDetected = false;
            lifeEvent.ReminderParseStatus = "none";
        }

        // Schema 强类型约束校验与过滤
        LifeEventSchemaValidator.ValidateAndSanitize(lifeEvent);

        var batch = _db.StartBatch();

        var eventDocRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events")
            .Document(eventId);

        batch.Set(eventDocRef, lifeEvent);

        if (reminder != null)
        {
            var reminderDocRef = _db
                .Collection("users")
                .Document(userId)
                .Collection("reminders")
                .Document(reminder.Id);

            batch.Set(reminderDocRef, reminder);
        }

        _logger.LogInformation(
            "开始执行 WriteBatch 双写。UserId={UserId}, EventId={EventId}, HasReminder={HasReminder}, ParseStatus={ParseStatus}",
            userId, eventId, reminder != null, lifeEvent.ReminderParseStatus);

        await batch.CommitAsync();

        _logger.LogInformation("WriteBatch 双写提交成功。");

        return (lifeEvent, reminder);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ListEventsAsync
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private class LifeEventCursor
    {
        public DateTime OccurredAt { get; set; }
        public string Id { get; set; } = string.Empty;
    }

    /// <inheritdoc/>
    public async Task<ListEventsResult> ListEventsAsync(
        string userId,
        string? type   = null,
        int     limit  = 20,
        string? cursor = null,
        string? tag    = null)
    {
        // ── 参数校验 ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        // 上限 100，下限 1
        limit = Math.Clamp(limit, 1, 100);

        // ── 构建基础查询 ───────────────────────────────────────────────
        var collection = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events");

        Query query = collection
            .WhereEqualTo("isDeleted", false);

        // ── 可选 type 过滤 ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(type)
            && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.WhereEqualTo("type", type);
        }

        // ── 可选 tag 过滤 ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(tag))
        {
            query = query.WhereArrayContains("tags", tag);
        }

        // 排序规则必须跟 OrderByDescending 声明完全一致以支撑 StartAfter 游标
        query = query
            .OrderByDescending("occurredAt")
            .OrderByDescending(FieldPath.DocumentId);

        // ── Cursor 游标定位 ────────────────────────────────────────────
        // 格式：Base64(JSON, {"occurredAt":"ISO","id":"eventId"})
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var jsonBytes = Convert.FromBase64String(cursor);
                var cursorObj = System.Text.Json.JsonSerializer.Deserialize<LifeEventCursor>(
                    jsonBytes,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }
                );

                if (cursorObj != null)
                {
                    var lastOccurredAt = cursorObj.OccurredAt.ToUniversalTime();
                    var lastDocId = cursorObj.Id;
                    var lastDocRef = collection.Document(lastDocId);

                    query = query.StartAfter(Timestamp.FromDateTime(lastOccurredAt), lastDocRef);

                    _logger.LogDebug(
                        "使用 cursor 翻页：lastOccurredAt={OccurredAt}, lastDocId={DocId}",
                        lastOccurredAt, lastDocId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "cursor 解码失败，忽略 cursor 从第一页查询");
            }
        }

        // ── 执行服务端查询（多取 1 条判断 hasMore）───────────────────────
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
            var last = pageItems[^1];
            var cursorObj = new LifeEventCursor
            {
                OccurredAt = last.OccurredAt.ToUniversalTime(),
                Id = last.Id
            };
            var jsonBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                cursorObj,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }
            );
            nextCursor = Convert.ToBase64String(jsonBytes);

            _logger.LogDebug("生成 nextCursor：occurredAt={OccurredAt}, id={Id}", cursorObj.OccurredAt, cursorObj.Id);
        }

        _logger.LogInformation(
            "ListEventsAsync (Firestore Server-side): userId={UserId}, type={Type}, tag={Tag}, limit={Limit}, 返回={Count}, hasMore={HasMore}",
            userId, type ?? "all", tag ?? "none", limit, pageItems.Count, hasMore);

        return new ListEventsResult
        {
            Data       = pageItems,
            NextCursor = nextCursor
        };
    }

    /// <inheritdoc/>
    public async Task<LifeEvent?> GetEventAsync(string userId, string eventId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(eventId))
        {
            return null;
        }

        var docRef = _db.Collection("users").Document(userId).Collection("life_events").Document(eventId);
        var snapshot = await docRef.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            return null;
        }

        var ev = snapshot.ConvertTo<LifeEvent>();
        if (ev.IsDeleted)
        {
            return null; // 软删除数据对外返回不存在
        }

        return ev;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateEventAsync(string userId, string eventId, LifeEvent updatedEvent)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("eventId 不能为空", nameof(eventId));

        var docRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events")
            .Document(eventId);

        var snapshot = await docRef.GetSnapshotAsync();
        if (!snapshot.Exists)
        {
            _logger.LogWarning("更新失败：事件 {EventId} 不存在 (UserId={UserId})", eventId, userId);
            return false;
        }

        var currentEvent = snapshot.ConvertTo<LifeEvent>();
        if (currentEvent.IsDeleted)
        {
            _logger.LogWarning("更新失败：事件 {EventId} 已经被软删除，禁止编辑 (UserId={UserId})", eventId, userId);
            return false;
        }

        // 只更新允许修改的业务字段，系统元数据字段绝对不信任前端传入的值
        currentEvent.Title = updatedEvent.Title;
        currentEvent.Content = updatedEvent.Content;
        currentEvent.Tags = updatedEvent.Tags ?? new();
        currentEvent.Importance = updatedEvent.Importance;
        currentEvent.StructuredData = updatedEvent.StructuredData ?? new();
        if (!string.IsNullOrEmpty(updatedEvent.Type))
        {
            currentEvent.Type = updatedEvent.Type;
        }

        // 自动更新 updatedAt
        currentEvent.UpdatedAt = DateTime.UtcNow;

        // Schema 强类型约束校验与过滤
        LifeEventSchemaValidator.ValidateAndSanitize(currentEvent);

        _logger.LogInformation("更新 Firestore 事件: users/{UserId}/life_events/{EventId}", userId, eventId);
        await docRef.SetAsync(currentEvent);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> SoftDeleteEventAsync(string userId, string eventId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("eventId 不能为空", nameof(eventId));

        var docRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("life_events")
            .Document(eventId);

        var snapshot = await docRef.GetSnapshotAsync();
        if (!snapshot.Exists)
        {
            _logger.LogWarning("删除失败：事件 {EventId} 不存在 (UserId={UserId})", eventId, userId);
            return false;
        }

        var currentEvent = snapshot.ConvertTo<LifeEvent>();
        if (currentEvent.IsDeleted)
        {
            _logger.LogWarning("删除失败：事件 {EventId} 已经处于软删除状态 (UserId={UserId})", eventId, userId);
            return false; // 重复删除，或视为已删除失败
        }

        var now = DateTime.UtcNow;
        currentEvent.IsDeleted = true;
        currentEvent.DeletedAt = now;
        currentEvent.UpdatedAt = now;

        _logger.LogInformation("软删除 Firestore 事件: users/{UserId}/life_events/{EventId}", userId, eventId);
        await docRef.SetAsync(currentEvent);
        return true;
    }
}
