using LifeAgent.Api.Models.Memories;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Memories;

public sealed class ReadOnlyMemoryContextProvider : IMemoryContextProvider
{
    private const int DefaultMaxResults = 3;
    private const int HardMaxResults = 10;
    private readonly IMemoryRetrievalService _retrieval;
    private readonly MemoryContextProviderOptions _options;

    public ReadOnlyMemoryContextProvider(
        IMemoryRetrievalService retrieval,
        IOptions<MemoryContextProviderOptions> options)
    {
        _retrieval = retrieval;
        _options = options.Value;
    }

    public async Task<MemoryRuntimeContext> GetContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableMemoryRetrieval || !_options.EnableMemoryContextInAgent)
        {
            return MemoryRuntimeContext.Disabled();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return MemoryRuntimeContext.Skipped("missing_user");
        }

        if (!IsAllowed(request.UserId, _options.UserAllowlist))
        {
            return MemoryRuntimeContext.Skipped("user_not_allowed");
        }

        if (!IsAllowed(request.Intent, _options.IntentAllowlist))
        {
            return MemoryRuntimeContext.Skipped("intent_not_allowed");
        }

        var maxResults = NormalizeMaxResults(_options.MaxResults);
        if (maxResults <= 0)
        {
            return MemoryRuntimeContext.Skipped("max_results_zero");
        }

        try
        {
            var results = await _retrieval.RetrieveAsync(new MemoryRetrievalRequest
            {
                UserId = request.UserId,
                Query = request.AgentRequest.Message,
                Limit = maxResults,
                Statuses = new[] { MemoryStatus.Active.ToSnakeCaseString() },
                IncludeArchived = false
            }, cancellationToken);

            var items = results
                .Take(maxResults)
                .Select(result => new MemoryRuntimeContextItem
                {
                    MemoryId = result.MemoryId,
                    MemoryType = result.MemoryType,
                    Score = result.Score,
                    Reason = result.Reason
                })
                .ToArray();

            return new MemoryRuntimeContext
            {
                Enabled = true,
                Status = items.Length == 0 ? "empty" : "ready",
                ResultCount = items.Length,
                MaxResults = maxResults,
                FormattedContext = FormatContext(results.Take(maxResults)),
                Results = items
            };
        }
        catch
        {
            return MemoryRuntimeContext.Skipped("retrieval_failed");
        }
    }

    private static int NormalizeMaxResults(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value == 0)
        {
            return DefaultMaxResults;
        }

        return Math.Min(value, HardMaxResults);
    }

    private static bool IsAllowed(string value, IReadOnlyList<string> allowlist)
    {
        return allowlist.Count == 0 ||
               allowlist.Any(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FormatContext(IEnumerable<MemoryRetrievalResult> results)
    {
        var lines = results
            .Select(result => $"- [{result.MemoryType}] {Redact(result.Content)}")
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string Redact(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var redacted = content
            .Replace("password", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("token", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("secret", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("apiKey", "[redacted]", StringComparison.OrdinalIgnoreCase);

        return redacted.Length <= 160 ? redacted : $"{redacted[..160]}...";
    }
}
