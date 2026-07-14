using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Reminders;

public sealed class Phase80ReminderConfirmWriteExecutor : IPhase80ConfirmWriteExecutor
{
    private const string ExecutorId = "phase80_reminder_confirm_write_executor";
    private readonly IReminderService _reminderService;
    private readonly ILlmService _llmService;

    public Phase80ReminderConfirmWriteExecutor(
        IReminderService reminderService,
        ILlmService llmService)
    {
        _reminderService = reminderService;
        _llmService = llmService;
    }

    public Phase80ConfirmWriteExecutorReadiness GetReadiness(Phase80ConfirmExecutionPlan plan)
    {
        var ready = string.Equals(plan.Target, Phase80PendingActionRuntime.ConfirmTargetReminders, StringComparison.Ordinal);
        return new Phase80ConfirmWriteExecutorReadiness(
            ExecutionReady: ready,
            RealPathReady: ready,
            ExecutorId: ExecutorId,
            Reason: ready
                ? "reminder_confirm_write_executor_ready"
                : "executor_only_supports_reminders");
    }

    public async Task<Phase80ConfirmWriteExecutionResult> ExecuteAsync(
        Phase80ConfirmExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Plan.Target, Phase80PendingActionRuntime.ConfirmTargetReminders, StringComparison.Ordinal) ||
            !string.Equals(request.ActionType, Phase80PendingActionRuntime.ReminderPreview, StringComparison.Ordinal))
        {
            return Skipped(request, "unsupported_confirm_write_target");
        }

        var rawText = CleanReminderText(request.Summary, request.Title);
        var timeZone = string.IsNullOrWhiteSpace(request.ClientTimeZone)
            ? "Asia/Shanghai"
            : request.ClientTimeZone.Trim();
        var parsed = await _llmService.ParseAsync(rawText, timeZone);

        if (!parsed.DetectedReminderIntent && parsed.Reminder?.HasIntent != true)
        {
            return Skipped(request, "missing_due_time", status: "missing_due_time");
        }

        var dueAtIso = parsed.Reminder?.DueAtIso8601 ?? parsed.ReminderDueAtIso;
        if (string.IsNullOrWhiteSpace(dueAtIso))
        {
            return Skipped(request, "reminder_due_time_missing", status: "missing_due_time");
        }

        if (!DateTime.TryParse(dueAtIso, out var dueAt))
        {
            return Skipped(request, "reminder_due_time_invalid", status: "invalid_due_time");
        }

        var now = DateTime.UtcNow;
        var reminder = new Reminder
        {
            Id = $"rem_{Guid.NewGuid():N}",
            UserId = request.UserId,
            SourceEventId = request.ActionId,
            Title = FirstNonEmpty(parsed.Reminder?.Title, parsed.ReminderTitle, CleanReminderTitle(request.Title), rawText),
            Description = FirstNonEmpty(parsed.Reminder?.Description, parsed.ReminderDescription, rawText),
            DueAt = dueAt.ToUniversalTime(),
            Timezone = timeZone,
            Status = "pending",
            RepeatRule = "none",
            CreatedAt = now,
            UpdatedAt = now,
            LlmConfidence = parsed.ExtractionConfidence,
            RawText = rawText
        };

        var created = await _reminderService.CreateReminderAsync(request.UserId, reminder, cancellationToken);

        return new Phase80ConfirmWriteExecutionResult(
            Success: true,
            Status: "written",
            Target: request.Plan.Target,
            ResourcePath: $"users/{request.UserId}/reminders/{created.Id}",
            WroteData: true,
            RealWritePath: true,
            ExecutorId: ExecutorId,
            Reason: "reminder_written_after_confirm");
    }

    private static Phase80ConfirmWriteExecutionResult Skipped(
        Phase80ConfirmExecutionRequest request,
        string reason,
        string status = "skipped")
    {
        return new Phase80ConfirmWriteExecutionResult(
            Success: false,
            Status: status,
            Target: request.Plan.Target,
            ResourcePath: null,
            WroteData: false,
            RealWritePath: false,
            ExecutorId: ExecutorId,
            Reason: reason);
    }

    private static string CleanReminderText(string summary, string title)
    {
        var value = string.IsNullOrWhiteSpace(summary) ? title : summary;
        const string userInputPrefix = "用户输入：";
        if (value.StartsWith(userInputPrefix, StringComparison.Ordinal))
        {
            value = value[userInputPrefix.Length..].Trim();
        }

        return value.Trim();
    }

    private static string CleanReminderTitle(string title)
    {
        const string reminderPrefix = "提醒：";
        return title.StartsWith(reminderPrefix, StringComparison.Ordinal)
            ? title[reminderPrefix.Length..].Trim()
            : title.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "提醒事项";
    }
}
