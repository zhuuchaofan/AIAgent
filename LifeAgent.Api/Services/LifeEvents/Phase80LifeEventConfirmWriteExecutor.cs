using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.LifeEvents;

public sealed class Phase80LifeEventConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    private readonly IAgentLifeEventWriter _writer;

    public Phase80LifeEventConfirmWriteExecutor(IAgentLifeEventWriter writer)
    {
        _writer = writer;
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        var ready = string.Equals(plan.Target, Phase80PendingActionRuntime.ConfirmTargetLifeEvents, StringComparison.Ordinal);
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: ready,
            RealPathReady: ready,
            ExecutorId: "phase80_life_event_confirm_write_executor",
            Reason: ready
                ? "life_event_confirm_write_executor_ready"
                : "executor_only_supports_life_events");
    }

    public async Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Plan.Target, Phase80PendingActionRuntime.ConfirmTargetLifeEvents, StringComparison.Ordinal) ||
            !string.Equals(request.ActionType, Phase80PendingActionRuntime.LifeRecordPreview, StringComparison.Ordinal))
        {
            return new Phase80ConfirmWriteExecutionResult(
                Success: false,
                Status: "skipped",
                Target: request.Plan.Target,
                ResourcePath: null,
                WroteData: false,
                RealWritePath: false,
                ExecutorId: "phase80_life_event_confirm_write_executor",
                Reason: "unsupported_confirm_write_target");
        }

        var createRequest = new CreateLifeEventRequest
        {
            Type = "life",
            Title = request.Title,
            Content = CleanLifeRecordContent(request.Summary)
        };
        var lifeEvent = AgentLifeEventFactory.Create(request.UserId, request.ActionId, createRequest);
        await _writer.WriteAsync(request.UserId, lifeEvent.Id, lifeEvent, cancellationToken);

        return new Phase80ConfirmWriteExecutionResult(
            Success: true,
            Status: "written",
            Target: request.Plan.Target,
            ResourcePath: $"users/{request.UserId}/life_events/{lifeEvent.Id}",
            WroteData: true,
            RealWritePath: true,
            ExecutorId: "phase80_life_event_confirm_write_executor",
            Reason: "life_event_written_after_confirm");
    }

    private static string CleanLifeRecordContent(string value)
    {
        var content = value.Trim();
        const string userInputPrefix = "用户输入：";
        if (content.StartsWith(userInputPrefix, StringComparison.Ordinal))
        {
            content = content[userInputPrefix.Length..].Trim();
        }

        var safetyNoteIndex = content.IndexOf("。生活记录", StringComparison.Ordinal);
        if (safetyNoteIndex >= 0)
        {
            content = content[..safetyNoteIndex].Trim();
        }

        return content.TrimEnd('。', ' ');
    }
}
