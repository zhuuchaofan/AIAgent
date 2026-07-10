using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Services.Agent.Phase8;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

public class Phase80PendingActionRuntimeMvpTest
{
    [Fact]
    public void CreateProducesPendingActionForAuthenticatedUserOnly()
    {
        var runtime = new Phase80PendingActionRuntime();

        var result = runtime.Create("user_a", new Phase80CreatePendingActionRequest("测试动作", "等待确认"));

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("pending", result.Data!.Status);
        Assert.Equal("测试动作", result.Data.Title);
        Assert.False(result.Data.Executed);
        Assert.False(result.Data.WroteData);
        Assert.False(result.Data.ExecutionReady);
        Assert.Equal("deny_all_no_real_execution", result.Data.GuardDecision);
        Assert.Equal("phase8_fake_first_in_memory", result.Data.SafetyMode);
        Assert.False(result.Data.LegacyConfirmEndpointUsed);
        Assert.False(result.Data.RealWritePath);
    }

    [Fact]
    public void ConfirmChangesStatusButDoesNotExecute()
    {
        var runtime = new Phase80PendingActionRuntime();
        var created = runtime.Create("user_a", new Phase80CreatePendingActionRequest(null, null));

        var confirmed = runtime.Confirm("user_a", created.Data!.ActionId);

        Assert.True(confirmed.Success);
        Assert.Equal("confirmed", confirmed.Status);
        Assert.NotNull(confirmed.Data);
        Assert.Equal("confirmed", confirmed.Data!.Status);
        Assert.False(confirmed.Data.Executed);
        Assert.False(confirmed.Data.WroteData);
        Assert.False(confirmed.Data.ExecutionReady);
        Assert.False(confirmed.Data.LegacyConfirmEndpointUsed);
        Assert.False(confirmed.Data.RealWritePath);
        Assert.Contains("未执行", confirmed.Message);
    }

    [Fact]
    public void CancelChangesStatusAndBlocksLaterConfirm()
    {
        var runtime = new Phase80PendingActionRuntime();
        var created = runtime.Create("user_a", new Phase80CreatePendingActionRequest(null, null));

        var cancelled = runtime.Cancel("user_a", created.Data!.ActionId);
        var confirmedAfterCancel = runtime.Confirm("user_a", created.Data.ActionId);

        Assert.True(cancelled.Success);
        Assert.Equal("cancelled", cancelled.Data!.Status);
        Assert.False(cancelled.Data.Executed);
        Assert.False(cancelled.Data.RealWritePath);
        Assert.False(confirmedAfterCancel.Success);
        Assert.Equal("cancelled", confirmedAfterCancel.Status);
    }

    [Fact]
    public void ExpiredActionCannotConfirm()
    {
        var runtime = new Phase80PendingActionRuntime();
        var expired = new Phase80PendingActionRecord(
            ActionId: "expired_action",
            UserId: "user_a",
            Status: Phase80PendingActionRuntime.Pending,
            Title: "过期动作",
            Summary: "应拒绝确认",
            ActionType: "phase8_fake_pending_action",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            ConfirmedAt: null,
            CancelledAt: null,
            Executed: false,
            WroteData: false,
            ExecutionReady: false,
            GuardDecision: "deny_all_no_real_execution");
        runtime.SeedForTests(expired);

        var result = runtime.Confirm("user_a", "expired_action");

        Assert.False(result.Success);
        Assert.Equal("expired", result.Status);
        Assert.Equal("expired", result.Data!.Status);
        Assert.False(result.Data.Executed);
        Assert.False(result.Data.RealWritePath);
    }

    [Fact]
    public void CrossUserConfirmIsBlockedAsNotFound()
    {
        var runtime = new Phase80PendingActionRuntime();
        var created = runtime.Create("user_a", new Phase80CreatePendingActionRequest(null, null));

        var result = runtime.Confirm("user_b", created.Data!.ActionId);

        Assert.False(result.Success);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public void ListReturnsOnlyOwnerActions()
    {
        var runtime = new Phase80PendingActionRuntime();
        var userA = runtime.Create("user_a", new Phase80CreatePendingActionRequest("A", null));
        runtime.Create("user_b", new Phase80CreatePendingActionRequest("B", null));

        var actions = runtime.List("user_a");

        Assert.Single(actions);
        Assert.Equal(userA.Data!.ActionId, actions[0].ActionId);
    }

    [Fact]
    public void Phase8DemoResultDeclaresItDoesNotUseLegacyConfirmOrRealWritePath()
    {
        var runtime = new Phase80PendingActionRuntime();
        var created = runtime.Create("user_a", new Phase80CreatePendingActionRequest("演示动作", "不携带执行 payload"));

        var confirmed = runtime.Confirm("user_a", created.Data!.ActionId);

        Assert.True(confirmed.Success);
        Assert.Equal("phase8_fake_first_in_memory", confirmed.Data!.SafetyMode);
        Assert.False(confirmed.Data.LegacyConfirmEndpointUsed);
        Assert.False(confirmed.Data.RealWritePath);
        Assert.False(confirmed.Data.Executed);
        Assert.False(confirmed.Data.WroteData);
        Assert.False(confirmed.Data.ExecutionReady);
    }

    [Fact]
    public async Task Phase8DemoEndpointsRejectUnauthenticatedRequests()
    {
        var context = new DefaultHttpContext();

        var create = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(context, new Phase80CreatePendingActionRequest(null, null)));
        var list = await ExecuteResultAsync(AgentEndpoints.ListPhase80PendingActionsAsync(context));
        var confirm = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(context, "missing"));
        var cancel = await ExecuteResultAsync(AgentEndpoints.CancelPhase80PendingActionAsync(context, "missing"));

        Assert.Equal(StatusCodes.Status401Unauthorized, create.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, list.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, confirm.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, cancel.StatusCode);
        Assert.Contains("Unauthorized", create.Body);
    }

    [Fact]
    public async Task Phase8DemoEndpointConfirmKeepsConfirmedSeparateFromExecuted()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var context = AuthenticatedContext(userId);

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            context,
            new Phase80CreatePendingActionRequest("endpoint demo", "no real write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext(userId),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executionReady"));
        Assert.False(ReadBool(confirmResult.Body, "data", "legacyConfirmEndpointUsed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Contains("未执行", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task Phase8DemoEndpointCancelBlocksLaterConfirmWithoutLegacyWritePath()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId),
            new Phase80CreatePendingActionRequest("cancel demo", "no memories or life_events write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var cancelResult = await ExecuteResultAsync(AgentEndpoints.CancelPhase80PendingActionAsync(
            AuthenticatedContext(userId),
            actionId));
        var confirmAfterCancel = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext(userId),
            actionId));

        Assert.Equal("cancelled", ReadString(cancelResult.Body, "data", "status"));
        Assert.False(ReadBool(cancelResult.Body, "data", "executed"));
        Assert.False(ReadBool(cancelResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(cancelResult.Body, "data", "legacyConfirmEndpointUsed"));
        Assert.False(ReadBool(cancelResult.Body, "data", "realWritePath"));
        Assert.False(ReadBool(confirmAfterCancel.Body, "success"));
        Assert.Equal("cancelled", ReadString(confirmAfterCancel.Body, "status"));
        Assert.False(ReadBool(confirmAfterCancel.Body, "data", "executed"));
        Assert.False(ReadBool(confirmAfterCancel.Body, "data", "wroteData"));
    }

    private static DefaultHttpContext AuthenticatedContext(string userId)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = TestServices;
        context.Items["userId"] = userId;
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = TestServices;
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

    private static string ReadString(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        var element = ReadElement(document.RootElement, path);
        return element.GetString() ?? string.Empty;
    }

    private static bool ReadBool(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        var element = ReadElement(document.RootElement, path);
        return element.GetBoolean();
    }

    private static JsonElement ReadElement(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            Assert.True(current.TryGetProperty(segment, out var next), $"Missing JSON property '{segment}' in {root}.");
            current = next;
        }

        return current;
    }

    private static readonly IServiceProvider TestServices = new ServiceCollection()
        .AddLogging()
        .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { })
        .BuildServiceProvider();
}
