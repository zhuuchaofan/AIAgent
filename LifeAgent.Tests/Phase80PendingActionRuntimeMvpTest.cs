using LifeAgent.Api.Services.Agent.Phase8;

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
}
