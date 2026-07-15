using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.Plans;

public sealed class FirestorePlanSignalService : IPlanSignalService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<FirestorePlanSignalService> _logger;

    public FirestorePlanSignalService(FirestoreDb db, ILogger<FirestorePlanSignalService> logger)
    {
        _db = db;
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

    public async Task<bool> ArchiveAsync(
        string userId,
        string signalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(signalId))
        {
            return false;
        }

        var docRef = _db
            .Collection("users")
            .Document(userId)
            .Collection("plan_signals")
            .Document(signalId);
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
}
