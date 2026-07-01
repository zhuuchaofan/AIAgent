using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Microsoft.Extensions.Options;

namespace LifeAgent.Tests;

public class MemoryContextProviderTest
{
    [Fact]
    public async Task Provider_FlagOff_DoesNotCallRetrieval()
    {
        var retrieval = new RecordingMemoryRetrievalService();
        var provider = new ReadOnlyMemoryContextProvider(
            retrieval,
            Options.Create(MemoryContextProviderOptions.Disabled));

        var context = await provider.GetContextAsync(Request());

        Assert.False(context.Enabled);
        Assert.Equal("disabled", context.Status);
        Assert.Equal("feature_disabled", context.SkippedReason);
        Assert.Equal(0, retrieval.CallCount);
    }

    [Fact]
    public async Task Provider_Enabled_AppliesMaxResultsAndReturnsSafeDiagnostics()
    {
        var retrieval = new RecordingMemoryRetrievalService
        {
            Results =
            [
                Result("mem_1", "preference", "喜欢早上写代码"),
                Result("mem_2", "goal", "完成 Phase 6"),
                Result("mem_3", "habit", "每天复盘")
            ]
        };
        var provider = new ReadOnlyMemoryContextProvider(
            retrieval,
            Options.Create(Enabled(maxResults: 2, intents: ["rag"])));

        var context = await provider.GetContextAsync(Request(intent: "rag", message: "写代码"));

        Assert.True(context.Enabled);
        Assert.Equal("ready", context.Status);
        Assert.Equal(2, context.ResultCount);
        Assert.Equal(2, context.MaxResults);
        Assert.Equal(1, retrieval.CallCount);
        Assert.Equal(2, retrieval.LastRequest!.Limit);
        Assert.Equal("active", Assert.Single(retrieval.LastRequest.Statuses!));

        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(context.ToDiagnostics());
        Assert.Contains("\"enabled\":true", diagnosticsJson);
        Assert.Contains("\"resultCount\":2", diagnosticsJson);
        Assert.DoesNotContain("喜欢早上写代码", diagnosticsJson);
    }

    [Fact]
    public async Task Provider_UsesRetrievalDefaultsToExcludeArchivedAndExpiredTemporaryContext()
    {
        var repository = new InMemoryMemoryRepository();
        await repository.CreateAsync("user_a", Memory("active_preference", MemoryType.Preference, MemoryStatus.Active));
        await repository.CreateAsync("user_a", Memory("archived_goal", MemoryType.Goal, MemoryStatus.Archived));
        await repository.CreateAsync("user_a", Memory(
            "expired_context",
            MemoryType.TemporaryContext,
            MemoryStatus.Active,
            expiresAt: DateTime.UtcNow.AddMinutes(-5)));
        var retrieval = new InMemoryMemoryRetrievalService(repository);
        var provider = new ReadOnlyMemoryContextProvider(
            retrieval,
            Options.Create(Enabled(maxResults: 5, intents: ["rag"])));

        var context = await provider.GetContextAsync(Request(intent: "rag", message: string.Empty));

        Assert.True(context.Enabled);
        Assert.Equal("ready", context.Status);
        Assert.Single(context.Results);
        Assert.Equal("preference", context.Results[0].MemoryType);
    }

    [Fact]
    public async Task Provider_RetrievalFailureFallsBackToSkippedContext()
    {
        var provider = new ReadOnlyMemoryContextProvider(
            new ThrowingMemoryRetrievalService(),
            Options.Create(Enabled(maxResults: 3, intents: ["rag"])));

        var context = await provider.GetContextAsync(Request(intent: "rag"));

        Assert.True(context.Enabled);
        Assert.Equal("skipped", context.Status);
        Assert.Equal("retrieval_failed", context.SkippedReason);
    }

    private static MemoryContextRequest Request(string intent = "rag", string message = "hello")
    {
        return new MemoryContextRequest
        {
            UserId = "user_a",
            Intent = intent,
            ActionType = "preview_readonly_rag",
            AgentRequest = new AgentRunRequest { Message = message }
        };
    }

    private static MemoryContextProviderOptions Enabled(int maxResults, IReadOnlyList<string> intents)
    {
        return new MemoryContextProviderOptions
        {
            EnableMemoryRetrieval = true,
            EnableMemoryContextInAgent = true,
            MaxResults = maxResults,
            IntentAllowlist = intents
        };
    }

    private static MemoryRetrievalResult Result(string id, string type, string content)
    {
        return new MemoryRetrievalResult
        {
            MemoryId = id,
            MemoryType = type,
            Content = content,
            Importance = 3,
            Confidence = 0.8,
            Score = 1,
            Source = "test",
            Reason = "test"
        };
    }

    private static Memory Memory(
        string content,
        MemoryType type,
        MemoryStatus status,
        DateTime? expiresAt = null)
    {
        return new Memory
        {
            Type = type.ToSnakeCaseString(),
            Status = status.ToSnakeCaseString(),
            Content = content,
            Importance = 3,
            Confidence = 0.8,
            Source = "test",
            ExpiresAt = expiresAt
        };
    }

    private sealed class RecordingMemoryRetrievalService : IMemoryRetrievalService
    {
        public int CallCount { get; private set; }
        public MemoryRetrievalRequest? LastRequest { get; private set; }
        public IReadOnlyList<MemoryRetrievalResult> Results { get; init; } = Array.Empty<MemoryRetrievalResult>();

        public Task<IReadOnlyList<MemoryRetrievalResult>> RetrieveAsync(
            MemoryRetrievalRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(Results);
        }
    }

    private sealed class ThrowingMemoryRetrievalService : IMemoryRetrievalService
    {
        public Task<IReadOnlyList<MemoryRetrievalResult>> RetrieveAsync(
            MemoryRetrievalRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
