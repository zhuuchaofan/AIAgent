using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.Phase8;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAgent.Tests;

public class Phase9PendingActionPersistenceTest
{
    [Fact]
    public async Task StoreCreateCanBeReadAfterRuntimeRefresh()
    {
        var store = new InMemoryPendingActionStore();
        var runtimeA = new Phase80PendingActionRuntime(store: store);
        var created = await runtimeA.CreateAsync("user_a", new Phase80CreatePendingActionRequest("持久动作", "刷新后仍可读取"));

        var runtimeB = new Phase80PendingActionRuntime(store: store);
        var restored = await runtimeB.ListAsync("user_a");

        Assert.True(created.Success);
        Assert.Single(restored);
        Assert.Equal(created.Data!.ActionId, restored[0].ActionId);
        Assert.Equal("pending", restored[0].Status);
        Assert.Equal("持久动作", restored[0].Title);
        Assert.False(restored[0].Executed);
        Assert.False(restored[0].WroteData);
    }

    [Fact]
    public async Task ConfirmStatusPersistsAndDoesNotExecute()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var confirmed = await runtime.ConfirmAsync("user_a", created.Data!.ActionId);
        var restored = await new Phase80PendingActionRuntime(store: store).ListAsync("user_a");

        Assert.True(confirmed.Success);
        Assert.Equal("confirmed", confirmed.Data!.Status);
        Assert.Single(restored);
        Assert.Equal("confirmed", restored[0].Status);
        Assert.False(restored[0].Executed);
        Assert.False(restored[0].WroteData);
        Assert.False(restored[0].ExecutionReady);
        Assert.False(restored[0].LegacyConfirmEndpointUsed);
        Assert.False(restored[0].RealWritePath);
    }

    [Fact]
    public async Task CancelStatusPersistsAndCannotConfirm()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var cancelled = await runtime.CancelAsync("user_a", created.Data!.ActionId);
        var confirmAfterCancel = await runtime.ConfirmAsync("user_a", created.Data.ActionId);
        var restored = await new Phase80PendingActionRuntime(store: store).ListAsync("user_a");

        Assert.True(cancelled.Success);
        Assert.Equal("cancelled", cancelled.Data!.Status);
        Assert.False(confirmAfterCancel.Success);
        Assert.Equal("cancelled", confirmAfterCancel.Status);
        Assert.Single(restored);
        Assert.Equal("cancelled", restored[0].Status);
        Assert.False(restored[0].Executed);
    }

    [Fact]
    public async Task CrossUserAccessIsBlocked()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var listForOtherUser = await runtime.ListAsync("user_b");
        var confirmForOtherUser = await runtime.ConfirmAsync("user_b", created.Data!.ActionId);

        Assert.Empty(listForOtherUser);
        Assert.False(confirmForOtherUser.Success);
        Assert.Equal("not_found", confirmForOtherUser.Status);
    }

    [Fact]
    public async Task EndpointIgnoresBodyUserIdAndUsesAuthContext()
    {
        var request = new Phase80CreatePendingActionRequest(
            "body cannot set owner",
            "malicious userId=user_b should not be trusted");
        var context = AuthenticatedContext("user_a");

        var create = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(context, request));
        var actionId = ReadString(create.Body, "data", "actionId");
        var userAList = await ExecuteResultAsync(AgentEndpoints.ListPhase80PendingActionsAsync(AuthenticatedContext("user_a")));
        var userBConfirm = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(AuthenticatedContext("user_b"), actionId));

        Assert.Equal(StatusCodes.Status200OK, create.StatusCode);
        Assert.Contains(actionId, userAList.Body);
        Assert.Equal("not_found", ReadString(userBConfirm.Body, "status"));
    }

    [Fact]
    public void FirestoreCandidateSerializesApprovedSchemaWithoutExecutionFlags()
    {
        var record = new PendingActionRecord
        {
            PendingActionId = "pa_schema",
            PreviewId = "preview_pa_schema",
            ToolId = "phase8_preview_tool",
            ToolVersion = "1.0",
            AdapterId = "phase8_preview_adapter",
            ActionType = "phase8_fake_pending_action",
            UserSubjectRef = "user_a",
            SessionSubjectRef = "agent_preview_default_session",
            RiskLevel = "low_preview_only",
            Status = PendingActionStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash = "idem_pa_schema",
            InputHash = "input_pa_schema",
            PreviewHash = "preview_pa_schema",
            PolicySnapshotRef = "policy_pa_schema",
            TraceId = "trace_pa_schema",
            AuditEventRefs = new[] { "audit_created", "audit_confirmed" },
            SanitizedPreviewRef = "preview_ref_pa_schema",
            ServerOnlyPayloadRef = "payload_ref_pa_schema",
            Payload = new Dictionary<string, string>
            {
                ["title"] = "schema title",
                ["summary"] = "schema summary"
            },
            WroteData = true,
            Executed = true
        };

        var document = FirestorePendingActionStore.ToDocument(record);

        Assert.Equal("pa_schema", document["pendingActionId"]);
        Assert.Equal("user_a", document["userId"]);
        Assert.Equal("phase8_fake_pending_action", document["actionType"]);
        Assert.Equal(PendingActionStatus.Confirmed, document["status"]);
        Assert.True(document.ContainsKey("payload"));
        Assert.True(document.ContainsKey("createdAt"));
        Assert.True(document.ContainsKey("updatedAt"));
        Assert.True(document.ContainsKey("confirmedAt"));
        Assert.True(document.ContainsKey("cancelledAt"));
        Assert.True(document.ContainsKey("audit"));
        Assert.False((bool)document["executed"]!);
        Assert.False((bool)document["wroteData"]!);
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
        var current = document.RootElement;
        foreach (var segment in path)
        {
            Assert.True(current.TryGetProperty(segment, out var next), $"Missing JSON property '{segment}'.");
            current = next;
        }

        return current.GetString() ?? string.Empty;
    }

    private static readonly IServiceProvider TestServices = new ServiceCollection()
        .AddLogging()
        .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { })
        .BuildServiceProvider();
}
