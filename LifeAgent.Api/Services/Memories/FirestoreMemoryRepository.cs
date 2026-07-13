using Google.Cloud.Firestore;
using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class FirestoreMemoryRepository : IMemoryRepository
{
    private const string CollectionName = "memories";
    private readonly FirestoreDb _db;
    private readonly TimeProvider _timeProvider;

    public FirestoreMemoryRepository(FirestoreDb db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Memory> CreateAsync(string userId, Memory memory)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (memory == null)
        {
            throw new ArgumentNullException(nameof(memory));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        memory.Id = $"mem_{Guid.NewGuid():N}";
        memory.UserId = userId;
        memory.Status = MemoryStatus.Active.ToSnakeCaseString();
        memory.CreatedAt = now;
        memory.UpdatedAt = now;
        memory.LastRecalledAt = null;
        memory.RecCount = 0;

        MemoryValidator.Validate(memory);

        await UserCollection(userId)
            .Document(memory.Id)
            .SetAsync(memory);

        return memory;
    }

    public async Task<Memory?> GetAsync(string userId, string memoryId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return null;
        }

        var snapshot = await UserCollection(userId)
            .Document(memoryId)
            .GetSnapshotAsync();

        return snapshot.Exists ? snapshot.ConvertTo<Memory>() : null;
    }

    public async Task<IReadOnlyList<Memory>> ListByUserAsync(string userId, string? type = null, string? status = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        Query query = UserCollection(userId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.WhereEqualTo("type", type);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.WhereEqualTo("status", status);
        }

        var snapshot = await query.GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => document.ConvertTo<Memory>())
            .ToArray();
    }

    public async Task<Memory> UpdateAsync(string userId, Memory memory)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (memory == null)
        {
            throw new ArgumentNullException(nameof(memory));
        }

        if (string.IsNullOrWhiteSpace(memory.Id))
        {
            throw new ArgumentException("memory id is required.", nameof(memory));
        }

        var existing = await GetAsync(userId, memory.Id);
        if (existing == null)
        {
            throw new KeyNotFoundException($"Memory {memory.Id} was not found for user {userId}.");
        }

        existing.Content = memory.Content;
        existing.Importance = memory.Importance;
        existing.ExpiresAt = memory.ExpiresAt;
        existing.Metadata = memory.Metadata;
        existing.Confidence = memory.Confidence;
        existing.Status = memory.Status;
        existing.SourceEventIds = memory.SourceEventIds;
        existing.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        MemoryValidator.Validate(existing);

        await UserCollection(userId)
            .Document(existing.Id)
            .SetAsync(existing);

        return existing;
    }

    public async Task<Memory> ArchiveAsync(string userId, string memoryId)
    {
        var existing = await GetAsync(userId, memoryId);
        if (existing == null)
        {
            throw new KeyNotFoundException($"Memory {memoryId} was not found for user {userId}.");
        }

        existing.Status = MemoryStatus.Archived.ToSnakeCaseString();
        existing.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await UserCollection(userId)
            .Document(existing.Id)
            .SetAsync(existing);

        return existing;
    }

    private CollectionReference UserCollection(string userId)
    {
        return _db.Collection("users")
            .Document(userId)
            .Collection(CollectionName);
    }
}
