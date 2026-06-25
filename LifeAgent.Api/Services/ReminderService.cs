using Google.Cloud.Firestore;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Services;

public class ReminderService : IReminderService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(FirestoreDb db, ILogger<ReminderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<Reminder>> ListRemindersAsync(string userId, string status)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));

        _logger.LogInformation("查询用户 {UserId} 状态为 {Status} 的提醒列表", userId, status);

        var query = _db.Collection("users")
            .Document(userId)
            .Collection("reminders")
            .WhereEqualTo("status", status)
            .OrderBy("dueAt");

        var snapshot = await query.GetSnapshotAsync();
        return snapshot.Documents.Select(d => d.ConvertTo<Reminder>()).ToList();
    }

    /// <inheritdoc/>
    public async Task<Reminder?> GetReminderAsync(string userId, string reminderId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(reminderId))
            return null;

        var docRef = _db.Collection("users")
            .Document(userId)
            .Collection("reminders")
            .Document(reminderId);

        var snapshot = await docRef.GetSnapshotAsync();
        if (!snapshot.Exists)
            return null;

        return snapshot.ConvertTo<Reminder>();
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateReminderAsync(string userId, string reminderId, string? status, DateTime? dueAt)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId 不能为空", nameof(userId));
        if (string.IsNullOrWhiteSpace(reminderId))
            throw new ArgumentException("reminderId 不能为空", nameof(reminderId));

        var docRef = _db.Collection("users")
            .Document(userId)
            .Collection("reminders")
            .Document(reminderId);

        var snapshot = await docRef.GetSnapshotAsync();
        if (!snapshot.Exists)
        {
            _logger.LogWarning("更新提醒失败：提醒 {ReminderId} 不存在 (UserId={UserId})", reminderId, userId);
            return false;
        }

        var current = snapshot.ConvertTo<Reminder>();

        bool statusChanged = ValidateUpdate(current, status, dueAt);

        // 5. 应用修改
        var now = DateTime.UtcNow;
        if (statusChanged)
        {
            current.Status = status!;
            current.UpdatedAt = now;
            if (status == "completed")
            {
                current.CompletedAt = now;
            }
            else if (status == "cancelled")
            {
                current.CancelledAt = now;
            }
        }

        if (dueAt != null)
        {
            current.DueAt = dueAt.Value.ToUniversalTime();
            current.UpdatedAt = now;
        }

        _logger.LogInformation(
            "更新 Firestore 提醒：users/{UserId}/reminders/{ReminderId}（status={Status}, dueAt={DueAt}）",
            userId, reminderId, current.Status, current.DueAt);

        await docRef.SetAsync(current);
        return true;
    }

    /// <summary>
    /// 提醒状态/时间修改校验逻辑（提取为静态方法以便于独立进行单元测试）
    /// </summary>
    public static bool ValidateUpdate(Reminder current, string? status, DateTime? dueAt)
    {
        // 1. 状态改变校验
        bool statusChanged = false;
        if (status != null)
        {
            if (status == "pending")
            {
                throw new InvalidInputException("不允许将状态改回 pending");
            }
            if (status != "completed" && status != "cancelled")
            {
                throw new InvalidInputException($"非法的状态值: {status}");
            }
            if (current.Status != status)
            {
                statusChanged = true;
            }
        }

        // 2. 单向不可逆规则：已完成或已取消状态，禁止更改为其它状态（若传入相同的值允许忽略或不操作）
        if (current.Status != "pending" && statusChanged)
        {
            throw new InvalidInputException($"提醒已处于 {current.Status} 状态，禁止更改为 {status}");
        }

        // 3. 时间与状态修改互斥规则：当请求试图将状态修改为 completed 或 cancelled 时，绝对不允许同时修改 dueAt
        if (statusChanged && dueAt != null)
        {
            throw new InvalidInputException("不允许在更改状态为 completed/cancelled 的同时修改 dueAt");
        }

        // 4. 非 pending 状态下修改 dueAt 校验：已处于 completed 或 cancelled 状态的提醒，再次尝试更新其 dueAt 的请求，均返回 400 Bad Request
        if (current.Status != "pending" && dueAt != null)
        {
            throw new InvalidInputException("已完成或已取消的提醒禁止修改 dueAt");
        }

        return statusChanged;
    }
}
