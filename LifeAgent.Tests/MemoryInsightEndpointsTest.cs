using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models.Exceptions;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services.Memories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAgent.Tests;

public class MemoryInsightEndpointsTest
{
    [Fact]
    public async Task UpdateMemoryItemAsync_UpdatesAllowedFieldsAndPreservesSystemFields()
    {
        var repository = new InMemoryMemoryRepository();
        var created = await repository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我喜欢宽松版型。",
            Importance = 3,
            Source = "memory_review_confirmed",
            SourceEventIds = new List<string> { "evt_1" },
            Confidence = 0.82
        });

        var result = await ExecuteResultAsync(MemoryInsightEndpoints.UpdateMemoryItemAsync(
            created.Id,
            new MemoryItemUpdateRequest
            {
                Content = "我更喜欢宽松但不拖沓的版型。",
                Type = MemoryType.Preference.ToSnakeCaseString(),
                Importance = 4
            },
            AuthenticatedContext("user_a"),
            repository));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.True(ReadBool(result.Body, "success"));
        Assert.Equal("我更喜欢宽松但不拖沓的版型。", ReadString(result.Body, "data", "content"));
        Assert.Equal(4, ReadInt(result.Body, "data", "importance"));

        var stored = await repository.GetAsync("user_a", created.Id);
        Assert.NotNull(stored);
        Assert.Equal("user_a", stored!.UserId);
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(created.CreatedAt, stored.CreatedAt);
        Assert.Equal("active", stored.Status);
        Assert.Equal("memory_review_confirmed", stored.Source);
        Assert.Equal(new[] { "evt_1" }, stored.SourceEventIds);
        Assert.Equal(0.82, stored.Confidence);
    }

    [Fact]
    public async Task UpdateMemoryItemAsync_ClearsExpiresAtForNonTemporaryContext()
    {
        var repository = new InMemoryMemoryRepository();
        var created = await repository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "这周在准备新疆旅行。",
            Importance = 3,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var result = await ExecuteResultAsync(MemoryInsightEndpoints.UpdateMemoryItemAsync(
            created.Id,
            new MemoryItemUpdateRequest
            {
                Content = "我正在准备新疆旅行。",
                Type = MemoryType.Goal.ToSnakeCaseString(),
                Importance = 4,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            },
            AuthenticatedContext("user_a"),
            repository));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        var stored = await repository.GetAsync("user_a", created.Id);
        Assert.NotNull(stored);
        Assert.Equal("goal", stored!.Type);
        Assert.Null(stored.ExpiresAt);
    }

    [Fact]
    public async Task UpdateMemoryItemAsync_RejectsInvalidTypeOrImportance()
    {
        var repository = new InMemoryMemoryRepository();
        var created = await repository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我喜欢宽松版型。",
            Importance = 3
        });

        await Assert.ThrowsAsync<InvalidInputException>(() => MemoryInsightEndpoints.UpdateMemoryItemAsync(
            created.Id,
            new MemoryItemUpdateRequest
            {
                Content = "新内容",
                Type = "not_a_type",
                Importance = 3
            },
            AuthenticatedContext("user_a"),
            repository));

        await Assert.ThrowsAsync<InvalidInputException>(() => MemoryInsightEndpoints.UpdateMemoryItemAsync(
            created.Id,
            new MemoryItemUpdateRequest
            {
                Content = "新内容",
                Type = MemoryType.Preference.ToSnakeCaseString(),
                Importance = 9
            },
            AuthenticatedContext("user_a"),
            repository));
    }

    [Fact]
    public async Task UpdateMemoryItemAsync_RejectsCrossUserUpdate()
    {
        var repository = new InMemoryMemoryRepository();
        var created = await repository.CreateAsync("user_a", new Memory
        {
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "Alice 的偏好。",
            Importance = 3
        });

        await Assert.ThrowsAsync<InvalidInputException>(() => MemoryInsightEndpoints.UpdateMemoryItemAsync(
            created.Id,
            new MemoryItemUpdateRequest
            {
                Content = "Bob 试图修改。",
                Type = MemoryType.Preference.ToSnakeCaseString(),
                Importance = 3
            },
            AuthenticatedContext("user_b"),
            repository));
    }

    [Fact]
    public void BuildQualityHints_FlagsDuplicateExpiringAndGenericMemories()
    {
        var expiring = new Memory
        {
            Id = "mem_expiring",
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我近期在准备新疆旅行路线。",
            Importance = 4,
            ExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        var duplicate = new Memory
        {
            Id = "mem_duplicate",
            Type = MemoryType.TemporaryContext.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我最近准备新疆旅行和路线。",
            Importance = 4,
            ExpiresAt = DateTime.UtcNow.AddDays(20)
        };
        var generic = new Memory
        {
            Id = "mem_generic",
            Type = MemoryType.Preference.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "计划",
            Importance = 3
        };

        var expiringHints = MemoryInsightEndpoints.BuildQualityHints(expiring, new[] { expiring, duplicate });
        var genericHints = MemoryInsightEndpoints.BuildQualityHints(generic, new[] { generic });

        Assert.Contains(expiringHints, hint => hint.Kind == "expiring_soon");
        Assert.Contains(expiringHints, hint => hint.Kind == "possible_duplicate");
        Assert.Contains(genericHints, hint => hint.Kind == "too_generic");
        Assert.All(expiringHints.Concat(genericHints), hint => Assert.False(string.IsNullOrWhiteSpace(hint.SuggestedAction)));
    }

    [Fact]
    public void BuildQualityHints_DoesNotFlagHealthySpecificMemory()
    {
        var memory = new Memory
        {
            Id = "mem_specific",
            Type = MemoryType.Goal.ToSnakeCaseString(),
            Status = MemoryStatus.Active.ToSnakeCaseString(),
            Content = "我计划在七月底前完成 LifeOS 最近回顾优化。",
            Importance = 4
        };

        var hints = MemoryInsightEndpoints.BuildQualityHints(memory, new[] { memory });

        Assert.Empty(hints);
    }

    private static DefaultHttpContext AuthenticatedContext(string userId)
    {
        var context = new DefaultHttpContext();
        context.Items["userId"] = userId;
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(Task<IResult> result)
    {
        return await ExecuteResultAsync(await result);
    }

    private static bool ReadBool(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        return ReadElement(document.RootElement, path).GetBoolean();
    }

    private static int ReadInt(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        return ReadElement(document.RootElement, path).GetInt32();
    }

    private static string ReadString(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        return ReadElement(document.RootElement, path).GetString() ?? string.Empty;
    }

    private static JsonElement ReadElement(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = current.GetProperty(segment);
        }

        return current;
    }
}
