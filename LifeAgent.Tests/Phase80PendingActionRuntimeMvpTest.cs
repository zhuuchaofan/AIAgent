using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.Phase8;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        Assert.Equal("personal_agent_v2_in_memory_preview_only", result.Data.SafetyMode);
        Assert.False(result.Data.LegacyConfirmEndpointUsed);
        Assert.False(result.Data.RealWritePath);
    }

    [Fact]
    public void CreateKeepsPreviewActionTypeWithoutExecution()
    {
        var runtime = new Phase80PendingActionRuntime();

        var lifeRecord = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest("生活记录：今天跑步三公里", "用户输入：今天跑步三公里", Phase80PendingActionRuntime.LifeRecordPreview));
        var reminder = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest("提醒：明天九点交材料", "用户输入：明天九点提醒我交材料", Phase80PendingActionRuntime.ReminderPreview));
        var confirmed = runtime.Confirm("user_a", lifeRecord.Data!.ActionId);

        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, lifeRecord.Data.ActionType);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, reminder.Data!.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, lifeRecord.Data.Intent);
        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, reminder.Data.Intent);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, lifeRecord.Data.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, reminder.Data.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, lifeRecord.Data.RiskLevel);
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, reminder.Data.RiskLevel);
        Assert.True(lifeRecord.Data.RequiresPendingAction);
        Assert.True(reminder.Data.RequiresPendingAction);
        Assert.Equal("requested_action_type", lifeRecord.Data.RouteReason);
        Assert.Equal("requested_action_type", reminder.Data.RouteReason);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, lifeRecord.Data.ConfirmTarget);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, reminder.Data.ConfirmTarget);
        Assert.False(lifeRecord.Data.ConfirmWriteEnabled);
        Assert.False(reminder.Data.ConfirmWriteEnabled);
        Assert.False(confirmed.Data!.Executed);
        Assert.False(confirmed.Data.WroteData);
        Assert.False(confirmed.Data.RealWritePath);
    }

    [Fact]
    public void CreateInfersReminderPreviewTypeWhenActionTypeIsMissing()
    {
        var runtime = new Phase80PendingActionRuntime();

        var result = runtime.Create("user_a", new Phase80CreatePendingActionRequest("提醒我", "明天上午九点提醒我交材料"));

        Assert.True(result.Success);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, result.Data!.ActionType);
        Assert.False(result.Data.Executed);
        Assert.False(result.Data.WroteData);
    }

    [Fact]
    public void PersonalHomeIntentRouterKeepsSingleInputPendingConfirmationFlow()
    {
        var lifeRecord = Phase80PersonalHomeIntentRouter.Route("今天跑步三公里", "感觉还不错", null);
        var reminder = Phase80PersonalHomeIntentRouter.Route("提醒我", "明天上午九点交材料", null);
        var explicitLifeRecord = Phase80PersonalHomeIntentRouter.Route(
            "提醒我也可以是一条记录",
            "用户明确选择生活记录",
            Phase80PendingActionRuntime.LifeRecordPreview);

        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, lifeRecord.Intent);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, lifeRecord.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.ReminderIntent, reminder.Intent);
        Assert.Equal(Phase80PendingActionRuntime.ReminderPreview, reminder.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LifeRecordIntent, explicitLifeRecord.Intent);
        Assert.Equal(Phase80PendingActionRuntime.LifeRecordPreview, explicitLifeRecord.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, lifeRecord.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, reminder.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, lifeRecord.RiskLevel);
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, reminder.RiskLevel);
    }

    [Fact]
    public void PersonalHomeIntentRouterKeepsUnknownExplicitActionsHighRisk()
    {
        var toolAction = Phase80PersonalHomeIntentRouter.Route(
            "帮我发一封邮件",
            "用户输入：发邮件给客户",
            "send_email_tool");

        Assert.Equal("send_email_tool", toolAction.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.ToolActionIntent, toolAction.Intent);
        Assert.Equal(Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition, toolAction.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.HighRisk, toolAction.RiskLevel);
        Assert.True(toolAction.RequiresPendingAction);
        Assert.Equal("requested_action_type", toolAction.Reason);
    }

    [Fact]
    public void PersonalHomeRoutingPolicyModelsFutureDirectSaveButDefaultsOff()
    {
        var previewOnly = Phase80PersonalHomeRoutingPolicy.DefaultPreviewOnly();
        var betaDirectSave = new Phase80PersonalHomeRoutingPolicy(AllowLowRiskDirectSave: true);

        var currentLifeRecord = previewOnly.Resolve(Phase80PersonalHomeIntentRouter.LifeRecordIntent);
        var betaLifeRecord = betaDirectSave.Resolve(Phase80PersonalHomeIntentRouter.LifeRecordIntent);
        var reminder = betaDirectSave.Resolve(Phase80PersonalHomeIntentRouter.ReminderIntent);
        var plan = betaDirectSave.Resolve(Phase80PersonalHomeIntentRouter.PlanIntent);
        var highRisk = betaDirectSave.Resolve("external_tool_call");

        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, currentLifeRecord.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, currentLifeRecord.RiskLevel);
        Assert.True(currentLifeRecord.RequiresPendingAction);
        Assert.Equal(Phase80PersonalHomeIntentRouter.DirectSaveDisposition, betaLifeRecord.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.LowRisk, betaLifeRecord.RiskLevel);
        Assert.False(betaLifeRecord.RequiresPendingAction);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, reminder.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, reminder.RiskLevel);
        Assert.True(reminder.RequiresPendingAction);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, plan.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, plan.RiskLevel);
        Assert.True(plan.RequiresPendingAction);
        Assert.Equal(Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition, highRisk.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.HighRisk, highRisk.RiskLevel);
        Assert.True(highRisk.RequiresPendingAction);
    }

    [Fact]
    public void PersonalHomeRouterInfersPlanAsMediumRiskPendingPreview()
    {
        var routed = Phase80PersonalHomeIntentRouter.Route("计划一下周末", "用户输入：周末去哪里", null);

        Assert.Equal(Phase80PersonalHomeIntentRouter.PlanIntent, routed.Intent);
        Assert.Equal(Phase80PendingActionRuntime.PlanPreview, routed.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, routed.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, routed.RiskLevel);
        Assert.True(routed.RequiresPendingAction);
    }

    [Fact]
    public void RuntimeKeepsUnknownExplicitActionsHighRiskAndPreviewOnly()
    {
        var runtime = new Phase80PendingActionRuntime();

        var created = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest("发送邮件", "用户输入：给客户发邮件", "send_email_tool"));

        Assert.True(created.Success);
        Assert.Equal("send_email_tool", created.Data!.ActionType);
        Assert.Equal(Phase80PersonalHomeIntentRouter.ToolActionIntent, created.Data.Intent);
        Assert.Equal(Phase80PersonalHomeIntentRouter.RequiredConfirmationDisposition, created.Data.Disposition);
        Assert.Equal(Phase80PersonalHomeIntentRouter.HighRisk, created.Data.RiskLevel);
        Assert.True(created.Data.RequiresPendingAction);
        Assert.False(created.Data.ConfirmWriteEnabled);
        Assert.False(created.Data.Executed);
        Assert.False(created.Data.WroteData);
        Assert.False(created.Data.RealWritePath);
    }

    [Fact]
    public void ConfirmUsesTypeSpecificPreviewMessagesWithoutWrites()
    {
        var runtime = new Phase80PendingActionRuntime();
        var lifeRecord = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest("生活记录：今天跑步三公里", "用户输入：今天跑步三公里", Phase80PendingActionRuntime.LifeRecordPreview));
        var reminder = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest("提醒：明天九点交材料", "用户输入：明天九点提醒我交材料", Phase80PendingActionRuntime.ReminderPreview));

        var confirmedLifeRecord = runtime.Confirm("user_a", lifeRecord.Data!.ActionId);
        var confirmedReminder = runtime.Confirm("user_a", reminder.Data!.ActionId);

        Assert.Contains("生活记录", confirmedLifeRecord.Message);
        Assert.Contains("life_events", confirmedLifeRecord.Message);
        Assert.Contains("提醒", confirmedReminder.Message);
        Assert.Contains("reminders", confirmedReminder.Message);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, confirmedLifeRecord.Data!.ConfirmTarget);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, confirmedReminder.Data!.ConfirmTarget);
        Assert.False(confirmedLifeRecord.Data.ConfirmWriteEnabled);
        Assert.False(confirmedReminder.Data.ConfirmWriteEnabled);
        Assert.True(confirmedLifeRecord.Data.MemoryCandidateOnly);
        Assert.True(confirmedReminder.Data.MemoryCandidateOnly);
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, confirmedLifeRecord.Data.MemoryTarget);
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, confirmedReminder.Data.MemoryTarget);
        Assert.False(confirmedLifeRecord.Data.MemoryWriteEnabled);
        Assert.False(confirmedReminder.Data.MemoryWriteEnabled);
        Assert.True(confirmedLifeRecord.Data.MemoryRequiresDedupe);
        Assert.True(confirmedLifeRecord.Data.MemoryRequiresMerge);
        Assert.True(confirmedLifeRecord.Data.MemoryRequiresConfirmation);
        Assert.False(confirmedLifeRecord.Data!.Executed);
        Assert.False(confirmedLifeRecord.Data.WroteData);
        Assert.False(confirmedLifeRecord.Data.RealWritePath);
        Assert.False(confirmedReminder.Data.Executed);
        Assert.False(confirmedReminder.Data.WroteData);
        Assert.False(confirmedReminder.Data.RealWritePath);
    }

    [Fact]
    public void ResolveConfirmExecutionPlanKeepsBetaWriteTargetsDefaultOff()
    {
        var lifeRecordPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(Phase80PendingActionRuntime.LifeRecordPreview);
        var reminderPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(Phase80PendingActionRuntime.ReminderPreview);
        var defaultPolicy = Phase80ConfirmWritePolicy.DefaultPreviewOnly();

        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, lifeRecordPlan.Target);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, reminderPlan.Target);
        Assert.False(defaultPolicy.AllowLifeEventWrites);
        Assert.False(defaultPolicy.AllowReminderWrites);
        Assert.False(lifeRecordPlan.WriteEnabled);
        Assert.False(reminderPlan.WriteEnabled);
        Assert.True(lifeRecordPlan.MemoryCandidateOnly);
        Assert.True(reminderPlan.MemoryCandidateOnly);
        Assert.Contains("beta_gate", lifeRecordPlan.Reason);
        Assert.Contains("beta_gate", reminderPlan.Reason);
    }

    [Fact]
    public void ResolveConfirmExecutionPlanCanModelBetaWritesWithoutExecutingThem()
    {
        var lifeOnly = new Phase80ConfirmWritePolicy(AllowLifeEventWrites: true, AllowReminderWrites: false);
        var reminderOnly = new Phase80ConfirmWritePolicy(AllowLifeEventWrites: false, AllowReminderWrites: true);

        var lifeRecordPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(
            Phase80PendingActionRuntime.LifeRecordPreview,
            lifeOnly);
        var reminderPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(
            Phase80PendingActionRuntime.ReminderPreview,
            reminderOnly);

        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, lifeRecordPlan.Target);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetReminders, reminderPlan.Target);
        Assert.True(lifeRecordPlan.WriteEnabled);
        Assert.True(reminderPlan.WriteEnabled);
        Assert.True(lifeRecordPlan.MemoryCandidateOnly);
        Assert.True(reminderPlan.MemoryCandidateOnly);
        Assert.Contains("allowed_by_policy", lifeRecordPlan.Reason);
        Assert.Contains("allowed_by_policy", reminderPlan.Reason);
    }

    [Fact]
    public void ConfirmWriteDecisionSeparatesPolicyFromExecutionReadiness()
    {
        var disabledPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(Phase80PendingActionRuntime.LifeRecordPreview);
        var enabledPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(
            Phase80PendingActionRuntime.LifeRecordPreview,
            new Phase80ConfirmWritePolicy(AllowLifeEventWrites: true, AllowReminderWrites: false));

        var disabledDecision = Phase80PendingActionRuntime.ResolveConfirmWriteDecision(disabledPlan);
        var enabledDecision = Phase80PendingActionRuntime.ResolveConfirmWriteDecision(enabledPlan);

        Assert.False(disabledDecision.ExecutionReady);
        Assert.False(disabledDecision.RealPathReady);
        Assert.Equal("confirm_write_disabled_by_policy", disabledDecision.Reason);
        Assert.False(enabledDecision.ExecutionReady);
        Assert.False(enabledDecision.RealPathReady);
        Assert.Equal("confirm_write_policy_enabled_but_executor_not_connected", enabledDecision.Reason);
    }

    [Fact]
    public void ConfirmWriteDecisionRequiresExplicitExecutorReadiness()
    {
        var enabledPlan = Phase80PendingActionRuntime.ResolveConfirmExecutionPlan(
            Phase80PendingActionRuntime.ReminderPreview,
            new Phase80ConfirmWritePolicy(AllowLifeEventWrites: false, AllowReminderWrites: true));

        var noOpDecision = Phase80PendingActionRuntime.ResolveConfirmWriteDecision(
            enabledPlan,
            Phase80NoOpConfirmWriteExecutor.Instance);
        var connectedDecision = Phase80PendingActionRuntime.ResolveConfirmWriteDecision(
            enabledPlan,
            new ReadyForTestConfirmWriteExecutor());

        Assert.False(noOpDecision.ExecutionReady);
        Assert.False(noOpDecision.RealPathReady);
        Assert.Equal("confirm_write_policy_enabled_but_executor_not_connected", noOpDecision.Reason);
        Assert.True(connectedDecision.ExecutionReady);
        Assert.True(connectedDecision.RealPathReady);
        Assert.Equal("test_executor_ready", connectedDecision.Reason);
    }

    [Fact]
    public void RuntimeCanExposeBetaWritePolicyWithoutExecutingWrites()
    {
        var runtime = new Phase80PendingActionRuntime(
            confirmWritePolicy: new Phase80ConfirmWritePolicy(
                AllowLifeEventWrites: true,
                AllowReminderWrites: false));
        var created = runtime.Create(
            "user_a",
            new Phase80CreatePendingActionRequest(
                "生活记录：读完一本书",
                "用户输入：今天读完一本书",
                Phase80PendingActionRuntime.LifeRecordPreview));

        var confirmed = runtime.Confirm("user_a", created.Data!.ActionId);

        Assert.True(created.Data.ConfirmWriteEnabled);
        Assert.True(confirmed.Data!.ConfirmWriteEnabled);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, confirmed.Data.ConfirmTarget);
        Assert.False(created.Data.ConfirmWriteExecutionReady);
        Assert.False(created.Data.ConfirmWriteRealPathReady);
        Assert.Equal("confirm_write_policy_enabled_but_executor_not_connected", created.Data.ConfirmWriteDecisionReason);
        Assert.False(confirmed.Data.ConfirmWriteExecutionReady);
        Assert.False(confirmed.Data.ConfirmWriteRealPathReady);
        Assert.Equal("confirm_write_policy_enabled_but_executor_not_connected", confirmed.Data.ConfirmWriteDecisionReason);
        Assert.False(confirmed.Data.Executed);
        Assert.False(confirmed.Data.WroteData);
        Assert.False(confirmed.Data.RealWritePath);
        Assert.True(confirmed.Data.MemoryCandidateOnly);
        Assert.False(confirmed.Data.MemoryWriteEnabled);
    }

    [Fact]
    public async Task ConfirmAndMemoryPlansAreStoredAsCreationSnapshots()
    {
        var store = new InMemoryPendingActionStore();
        var betaRuntime = new Phase80PendingActionRuntime(
            store: store,
            confirmWritePolicy: new Phase80ConfirmWritePolicy(
                AllowLifeEventWrites: true,
                AllowReminderWrites: false));
        var created = await betaRuntime.CreateAsync(
            "user_a",
            new Phase80CreatePendingActionRequest(
                "生活记录：今天写了计划",
                "用户输入：今天写了计划",
                Phase80PendingActionRuntime.LifeRecordPreview));
        var defaultRuntime = new Phase80PendingActionRuntime(store: store);

        var restored = await defaultRuntime.ListAsync("user_a");

        Assert.Single(restored);
        Assert.Equal(created.Data!.ActionId, restored[0].ActionId);
        Assert.True(restored[0].ConfirmWriteEnabled);
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetLifeEvents, restored[0].ConfirmTarget);
        Assert.Contains("allowed_by_policy", restored[0].ConfirmPlanReason);
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, restored[0].MemoryTarget);
        Assert.False(restored[0].MemoryWriteEnabled);
        Assert.True(restored[0].MemoryRequiresDedupe);
        Assert.True(restored[0].MemoryRequiresMerge);
        Assert.True(restored[0].MemoryRequiresConfirmation);
    }

    [Fact]
    public void ResolveMemoryPlanCreatesCandidateOnlyWithoutMemoryWrites()
    {
        var lifeRecordMemory = Phase80PendingActionRuntime.ResolveMemoryPlan(Phase80PendingActionRuntime.LifeRecordPreview);
        var reminderMemory = Phase80PendingActionRuntime.ResolveMemoryPlan(Phase80PendingActionRuntime.ReminderPreview);

        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, lifeRecordMemory.Target);
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, reminderMemory.Target);
        Assert.False(lifeRecordMemory.WriteEnabled);
        Assert.False(reminderMemory.WriteEnabled);
        Assert.True(lifeRecordMemory.RequiresDedupe);
        Assert.True(lifeRecordMemory.RequiresMerge);
        Assert.True(lifeRecordMemory.RequiresConfirmation);
        Assert.Contains("candidate_only", lifeRecordMemory.Reason);
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
        Assert.Equal("personal_agent_v2_in_memory_preview_only", confirmed.Data!.SafetyMode);
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
        var services = BuildServices();
        var context = AuthenticatedContext(userId, services);

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            context,
            new Phase80CreatePendingActionRequest("endpoint demo", "no real write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointKeepsUnknownExplicitActionHighRiskPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointConfirmUnknownExplicitActionStaysPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("发送邮件", "用户输入：给客户发邮件", "send_email_tool")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointInfersReminderFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointConfirmReminderStaysPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("提醒我", "明天上午九点提醒我交材料")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointInfersLifeRecordFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointConfirmLifeRecordStaysPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("今天跑步三公里", "用户输入：今天跑步三公里，感觉不错")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointInfersPlanFromUnifiedHomeInputPreviewOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();

        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("计划一下周末", "用户输入：计划周末去哪里")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, createResult.StatusCode);
        Assert.Equal(Phase80PendingActionRuntime.PlanPreview, ReadString(createResult.Body, "data", "actionType"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PlanIntent, ReadString(createResult.Body, "data", "intent"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.PendingConfirmationDisposition, ReadString(createResult.Body, "data", "disposition"));
        Assert.Equal(Phase80PersonalHomeIntentRouter.MediumRisk, ReadString(createResult.Body, "data", "riskLevel"));
        Assert.True(ReadBool(createResult.Body, "data", "requiresPendingAction"));
        Assert.Equal("inferred_from_home_input", ReadString(createResult.Body, "data", "routeReason"));
        Assert.Equal(Phase80PendingActionRuntime.ConfirmTargetNone, ReadString(createResult.Body, "data", "confirmTarget"));
        Assert.False(ReadBool(createResult.Body, "data", "confirmWriteEnabled"));
        Assert.Equal("confirmed", ReadString(confirmResult.Body, "data", "status"));
        Assert.False(ReadBool(confirmResult.Body, "data", "executed"));
        Assert.False(ReadBool(confirmResult.Body, "data", "wroteData"));
        Assert.False(ReadBool(confirmResult.Body, "data", "realWritePath"));
        Assert.Equal(Phase80PendingActionRuntime.MemoryCandidateTarget, ReadString(confirmResult.Body, "data", "memoryTarget"));
        Assert.False(ReadBool(confirmResult.Body, "data", "memoryWriteEnabled"));
        Assert.Contains("未写入计划数据", ReadString(confirmResult.Body, "message"));
    }

    [Fact]
    public async Task Phase8DemoEndpointConfirmKeepsMemoryCandidateOnly()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("今天跑步三公里", "用户输入：今天跑步三公里，感觉不错")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var confirmResult = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
    public async Task Phase8DemoEndpointCancelBlocksLaterConfirmWithoutLegacyWritePath()
    {
        var userId = $"endpoint_user_{Guid.NewGuid():N}";
        var services = BuildServices();
        var createResult = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            new Phase80CreatePendingActionRequest("cancel demo", "no memories or life_events write")));
        var actionId = ReadString(createResult.Body, "data", "actionId");

        var cancelResult = await ExecuteResultAsync(AgentEndpoints.CancelPhase80PendingActionAsync(
            AuthenticatedContext(userId, services),
            actionId));
        var confirmAfterCancel = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
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
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PendingActionPersistenceOptions>>().Value;
            return new Phase80PendingActionRuntime(
                timeProvider: sp.GetRequiredService<TimeProvider>(),
                store: sp.GetRequiredService<IPendingActionStore>(),
                safetyMode: options.SafetyMode);
        });
        return services.BuildServiceProvider();
    }

    private sealed class ReadyForTestConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
    {
        public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
        {
            return new Phase80ConfirmWriteExecutorReadiness(
                ExecutionReady: true,
                RealPathReady: true,
                Reason: "test_executor_ready");
        }
    }
}
