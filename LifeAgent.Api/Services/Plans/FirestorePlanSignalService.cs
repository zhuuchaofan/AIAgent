using Google.Cloud.Firestore;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Services.Plans;

public sealed class FirestorePlanSignalService : IPlanSignalService
{
    private readonly FirestoreDb _db;
    private readonly IReminderService _reminderService;
    private readonly ILogger<FirestorePlanSignalService> _logger;

    public FirestorePlanSignalService(
        FirestoreDb db,
        IReminderService reminderService,
        ILogger<FirestorePlanSignalService> logger)
    {
        _db = db;
        _reminderService = reminderService;
        _logger = logger;
    }

    public async Task<PlanSignal> CreateAsync(
        string userId,
        PlanSignal signal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId cannot be empty.", nameof(userId));
        }

        if (signal is null)
        {
            throw new ArgumentNullException(nameof(signal));
        }

        if (string.IsNullOrWhiteSpace(signal.Title))
        {
            throw new ArgumentException("Plan signal title cannot be empty.", nameof(signal));
        }

        var now = DateTime.UtcNow;
        signal.Id = string.IsNullOrWhiteSpace(signal.Id) ? $"plan_{Guid.NewGuid():N}" : signal.Id;
        signal.UserId = userId;
        signal.Kind = string.IsNullOrWhiteSpace(signal.Kind) ? "plan" : signal.Kind.Trim();
        signal.Status = string.IsNullOrWhiteSpace(signal.Status) ? "active" : signal.Status.Trim();
        signal.CreatedAt = signal.CreatedAt == default ? now : signal.CreatedAt.ToUniversalTime();
        signal.UpdatedAt = now;

        var docRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("plan_signals")
            .Document(signal.Id);

        _logger.LogInformation(
            "Creating plan signal: users/{UserId}/plan_signals/{SignalId} kind={Kind}",
            userId,
            signal.Id,
            signal.Kind);

        await docRef.SetAsync(signal, cancellationToken: cancellationToken);
        return signal;
    }

    public async Task<IReadOnlyList<PlanSignal>> ListAsync(
        string userId,
        string status = "active",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<PlanSignal>();
        }

        var finalStatus = string.IsNullOrWhiteSpace(status) ? "active" : status.Trim();
        var snapshot = await _db
            .Collection("users")
            .Document(userId)
            .Collection("plan_signals")
            .WhereEqualTo("status", finalStatus)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents
            .Select(document => document.ConvertTo<PlanSignal>())
            .OrderByDescending(signal => signal.CreatedAt)
            .ToList();
    }

    public async Task<PlanSignal?> GetAsync(
        string userId,
        string signalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(signalId))
        {
            return null;
        }

        var snapshot = await GetDocRef(userId, signalId).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? snapshot.ConvertTo<PlanSignal>() : null;
    }

    public async Task<bool> ArchiveAsync(
        string userId,
        string signalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(signalId))
        {
            return false;
        }

        var docRef = GetDocRef(userId, signalId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return false;
        }

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            ["status"] = "archived",
            ["archivedAt"] = DateTime.UtcNow,
            ["updatedAt"] = DateTime.UtcNow
        }, cancellationToken: cancellationToken);

        return true;
    }

    public async Task<PlanSignalReminderConversionResult?> ConvertReminderSignalAsync(
        string userId,
        string signalId,
        PlanSignalReminderConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(signalId))
        {
            throw new InvalidInputException("计划线索 ID 不能为空");
        }

        if (request.DueAt == default)
        {
            throw new InvalidInputException("提醒时间不能为空");
        }

        var docRef = GetDocRef(userId, signalId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return null;
        }

        var signal = snapshot.ConvertTo<PlanSignal>();
        if (!string.Equals(signal.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("这条线索已经处理过");
        }

        if (!string.Equals(signal.Kind, "reminder_signal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("只有提醒线索可以保存为提醒");
        }

        var title = FirstNonEmpty(request.Title, signal.Title, "提醒事项");
        var description = FirstNonEmpty(request.Description, signal.Content, title);
        var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "Asia/Shanghai" : request.Timezone.Trim();
        var reminder = await _reminderService.CreateReminderAsync(userId, new Reminder
        {
            Title = title,
            Description = description,
            DueAt = request.DueAt.ToUniversalTime(),
            Timezone = timezone,
            Status = "pending",
            RepeatRule = "none",
            SourceEventId = signal.SourceActionId,
            RawText = signal.Content,
            LlmConfidence = 0
        }, cancellationToken);

        var now = DateTime.UtcNow;
        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            ["status"] = "converted",
            ["convertedAt"] = now,
            ["convertedReminderId"] = reminder.Id,
            ["updatedAt"] = now
        }, cancellationToken: cancellationToken);

        signal.Status = "converted";
        signal.ConvertedAt = now;
        signal.ConvertedReminderId = reminder.Id;
        signal.UpdatedAt = now;

        return new PlanSignalReminderConversionResult(signal, reminder);
    }

    private DocumentReference GetDocRef(string userId, string signalId)
    {
        return _db
            .Collection("users")
            .Document(userId)
            .Collection("plan_signals")
            .Document(signalId);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
