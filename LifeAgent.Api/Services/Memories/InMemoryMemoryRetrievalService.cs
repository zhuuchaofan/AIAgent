using LifeAgent.Api.Models.Memories;

namespace LifeAgent.Api.Services.Memories;

public sealed class InMemoryMemoryRetrievalService : IMemoryRetrievalService
{
    private const int DefaultLimit = 5;
    private const int MaxLimit = 50;
    private readonly IMemoryRepository _repository;

    public InMemoryMemoryRetrievalService(IMemoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MemoryRetrievalResult>> RetrieveAsync(
        MemoryRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("userId is required for memory retrieval.", nameof(request));
        }

        var limit = NormalizeLimit(request.Limit);
        var types = NormalizeSet(request.Types);
        var statuses = NormalizeStatuses(request);
        var query = (request.Query ?? string.Empty).Trim();
        var queryTokens = Tokenize(query);

        var candidates = new List<Memory>();
        foreach (var status in statuses)
        {
            var memories = await _repository.ListByUserAsync(request.UserId, type: null, status: status);
            candidates.AddRange(memories);
        }

        var now = DateTime.UtcNow;
        return candidates
            .Where(memory => IsAllowedType(memory, types))
            .Where(memory => !IsExpiredTemporaryContext(memory, now))
            .Where(memory => MatchesQuery(memory, queryTokens))
            .Select(memory => ToResult(memory, queryTokens, now))
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Importance)
            .ThenByDescending(result => result.UpdatedAt ?? DateTime.MinValue)
            .ThenBy(result => result.MemoryId, StringComparer.Ordinal)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaxLimit);
    }

    private static HashSet<string> NormalizeSet(IReadOnlyList<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> NormalizeStatuses(MemoryRetrievalRequest request)
    {
        var requested = NormalizeSet(request.Statuses);
        if (requested.Count == 0)
        {
            requested.Add(MemoryStatus.Active.ToSnakeCaseString());
        }

        if (!request.IncludeArchived)
        {
            requested.Remove(MemoryStatus.Archived.ToSnakeCaseString());
        }

        return requested;
    }

    private static List<string> Tokenize(string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToList();
    }

    private static bool IsAllowedType(Memory memory, HashSet<string> types)
    {
        return types.Count == 0 || types.Contains(memory.Type);
    }

    private static bool IsExpiredTemporaryContext(Memory memory, DateTime now)
    {
        return string.Equals(memory.Type, MemoryType.TemporaryContext.ToSnakeCaseString(), StringComparison.OrdinalIgnoreCase) &&
               memory.ExpiresAt.HasValue &&
               memory.ExpiresAt.Value <= now;
    }

    private static bool MatchesQuery(Memory memory, IReadOnlyList<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return true;
        }

        return queryTokens.Any(token => memory.Content.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static MemoryRetrievalResult ToResult(Memory memory, IReadOnlyList<string> queryTokens, DateTime now)
    {
        var contentMatchScore = queryTokens.Count == 0
            ? 0.0
            : queryTokens.Count(token => memory.Content.Contains(token, StringComparison.OrdinalIgnoreCase)) * 10.0;
        var importanceScore = memory.Importance * 2.0;
        var confidenceScore = memory.Confidence;
        var recencyScore = CalculateRecencyScore(memory.UpdatedAt ?? memory.CreatedAt, now);
        var score = contentMatchScore + importanceScore + confidenceScore + recencyScore;

        return new MemoryRetrievalResult
        {
            MemoryId = memory.Id,
            MemoryType = memory.Type,
            Content = memory.Content,
            Confidence = memory.Confidence,
            Importance = memory.Importance,
            Score = Math.Round(score, 4),
            Source = memory.Source,
            UpdatedAt = memory.UpdatedAt ?? memory.CreatedAt,
            Reason = BuildReason(contentMatchScore, importanceScore, confidenceScore, recencyScore)
        };
    }

    private static double CalculateRecencyScore(DateTime timestamp, DateTime now)
    {
        if (timestamp == default)
        {
            return 0.0;
        }

        var age = now - timestamp;
        if (age < TimeSpan.Zero)
        {
            return 0.5;
        }

        return age <= TimeSpan.FromDays(7) ? 0.5 : 0.0;
    }

    private static string BuildReason(
        double contentMatchScore,
        double importanceScore,
        double confidenceScore,
        double recencyScore)
    {
        var reasons = new List<string>();
        if (contentMatchScore > 0)
        {
            reasons.Add("content_match");
        }

        reasons.Add($"importance:{importanceScore:0.##}");
        reasons.Add($"confidence:{confidenceScore:0.##}");
        if (recencyScore > 0)
        {
            reasons.Add("recent");
        }

        return string.Join(",", reasons);
    }
}
