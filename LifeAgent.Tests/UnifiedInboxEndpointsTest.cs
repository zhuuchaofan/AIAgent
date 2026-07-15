using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.Phase8;
using LifeAgent.Api.Services.Agent.UnifiedInbox;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeAgent.Tests;

public class UnifiedInboxEndpointsTest
{
    [Fact]
    public async Task EndpointsRejectUnauthenticatedRequests()
    {
        var context = new DefaultHttpContext();

        var create = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(context, new Phase80CreatePendingActionRequest(null, null)));
        var list = await ExecuteResultAsync(AgentEndpoints.ListUnifiedInboxPendingActionsAsync(context));
        var confirm = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(context, "missing"));
        var cancel = await ExecuteResultAsync(AgentEndpoints.CancelUnifiedInboxPendingActionAsync(context, "missing"));

        Assert.Equal(StatusCodes.Status401Unauthorized, create.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, list.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, confirm.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, cancel.StatusCode);
        Assert.Contains("Unauthorized", create.Body);
    }

    [Fact]
    public async Task ConfirmKeepsConfirmedSeparateFromExecuted()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var context = AuthenticatedContext(userId, services);

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            context,
            new Phase80CreatePendingActionRequest("endpoint demo", "no real write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, ReadString(confirmResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, ReadString(confirmResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, ReadString(confirmResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(confirmResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("inferred_from_home_input", ReadString(confirmResult.Body, "data", "routeReason"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executionReady"));
        Assert.False(ReadBool(confirmResult.Body, "data", "legacyConfirmEndpointUsed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Contains("未执行", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task KeepsUnknownExplicitActionHighRiskPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("发送邮件", "用户输入：给客户发邮件", "send_email_tool")));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal("send_email_tool", ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.ToolActionIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition, ReadString(createResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.HighRisk, ReadString(createResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(createResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("requested_action_type", ReadString(createResult.Body, "data", "routeReason"));
        Assert.False(ReadBool(createResult.Body, "data", "executed"));
        Assert.False(ReadBool(createResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(createResult.Body, "data", "realWritePath"));
        Assert.False(ReadBool(createResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(createResult.Body, "data", "memoryWriteEnabled"));
    }

    [Fact]
    public async Task ConfirmUnknownExplicitActionStaysPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("发送邮件", "用户输入：给客户发邮件", "send_email_tool")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.Equal("send_email_tool", ReadString(confirmResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.ToolActionIntent, ReadString(confirmResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition, ReadString(confirmResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.HighRisk, ReadString(confirmResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(confirmResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("requested_action_type", ReadString(confirmResult.Body, "data", "routeReason"));
        Assert.False(ReadBool(confirmResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.False(ReadBool(confirmResult.Body, "data", "memoryWriteEnabled"));
        Assert.Contains("未执行", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task InfersReminderFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("提醒我", "明天上午九点提醒我交材料")));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, ReadString(createResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, ReadString(createResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(createResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("inferred_from_home_input", ReadString(createResult.Body, "data", "routeReason"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, ReadString(createResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(createResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(createResult.Body, "data", "executed"));
        Assert.False(ReadBool(createResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(createResult.Body, "data", "realWritePath"));
    }

    [Fact]
    public async Task ConfirmReminderStaysPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("提醒我", "明天上午九点提醒我交材料")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, ReadString(confirmResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, ReadString(confirmResult.Body, "data", "intent"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, ReadString(confirmResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(confirmResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Contains("未写入 reminders", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task InfersLifeRecordFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("今天跑步三公里", "用户输入：今天跑步三公里，感觉不错")));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, ReadString(createResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, ReadString(createResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(createResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("inferred_from_home_input", ReadString(createResult.Body, "data", "routeReason"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, ReadString(createResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(createResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(createResult.Body, "data", "executed"));
        Assert.False(ReadBool(createResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(createResult.Body, "data", "realWritePath"));
    }

    [Fact]
    public async Task FutureTimeJournalStillRoutesToLifeRecordPreview()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("下周去新疆", "下周这个时候应该就在去新疆的路上啦，最近一直在准备")));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, ReadString(createResult.Body, "data", "confirmTarget"));
    }

    [Fact]
    public async Task ConfirmLifeRecordStaysPreviewOnlyInDefaultTestPolicy()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("今天跑步三公里", "用户输入：今天跑步三公里，感觉不错")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, ReadString(confirmResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, ReadString(confirmResult.Body, "data", "intent"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, ReadString(confirmResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(confirmResult.Body, "data", "confirmWriteEnabled"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Contains("未写入 life_events", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task InfersPlanFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("计划一下周末", "用户输入：计划周末去哪里")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.PlanPreview, ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PlanIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, ReadString(createResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, ReadString(createResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(createResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("inferred_from_home_input", ReadString(createResult.Body, "data", "routeReason"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetPlanSignals, ReadString(createResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(createResult.Body, "data", "confirmWriteEnabled"));
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, ReadString(confirmResult.Body, "data", "memoryTarget"));
        Assert.False(ReadBool(confirmResult.Body, "data", "memoryWriteEnabled"));
        Assert.Contains("未写入计划线索", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task ConfirmKeepsMemoryCandidateOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("今天跑步三公里", "用户输入：今天跑步三公里，感觉不错")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, confirmResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, ReadString(confirmResult.Body, "data", "memoryTarget"));
        Assert.True(ReadBool(confirmResult.Body, "data", "memoryCandidateOnly"));
        Assert.False(ReadBool(confirmResult.Body, "data", "memoryWriteEnabled"));
        Assert.True(ReadBool(confirmResult.Body, "data", "memoryRequiresDedupe"));
        Assert.True(ReadBool(confirmResult.Body, "data", "memoryRequiresMerge"));
        Assert.True(ReadBool(confirmResult.Body, "data", "memoryRequiresConfirmation"));
    }

    [Fact]
    public async Task CancelBlocksLaterConfirmWithoutLegacyWritePath()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreateUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("cancel demo", "no memories or life_events write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var cancelResult = await ExecuteResultAsync(AgentEndpoints.CancelUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));
        var confirmAfterCancel = await ExecuteResultAsync(AgentEndpoints.ConfirmUnifiedInboxPendingActionAsync(
            AuthenticatedContext(userId, services),
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

    private static DefaultHttpContext AuthenticatedContext(string userId, IServiceProvider? services = null)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = services ?? BuildServices();
        context.Items["userId"] = userId;
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = BuildServices();
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

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new PendingActionPersistenceOptions()));
        services.AddSingleton<IPendingActionStore>(sp =>
            new InMemoryPendingActionStore(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Phase80PendingActionRuntime>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PendingActionPersistenceOptions>>().Value;
            return new Phase80PendingActionRuntime(
                timeProvider: sp.GetRequiredService<TimeProvider>(),
                store: sp.GetRequiredService<IPendingActionStore>(),
                safetyMode: options.SafetyMode);
        });
        services.AddSingleton<IUnifiedInboxRuntime, UnifiedInboxRuntime>();
        return services.BuildServiceProvider();
    }
}
