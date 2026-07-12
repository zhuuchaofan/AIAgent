using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Agent.UnifiedInbox;

public sealed class LlmUnifiedInboxIntentClassifier : IUnifiedInboxIntentClassifier
{
    private readonly ILlmService _llmService;
    private readonly ILogger<LlmUnifiedInboxIntentClassifier> _logger;

    public LlmUnifiedInboxIntentClassifier(
        ILlmService llmService,
        ILogger<LlmUnifiedInboxIntentClassifier> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<Phase80PersonalHomeIntentRoute> ClassifyAsync(
        UnifiedInboxIntentClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedActionType))
        {
            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                request.RequestedActionType,
                "explicit_action_type");
        }

        var source = $"{request.Title}\n{request.Summary}";
        try
        {
            var parsed = await _llmService.ParseAsync(
                source,
                string.IsNullOrWhiteSpace(request.ClientTimeZone) ? "Asia/Shanghai" : request.ClientTimeZone);

            if (parsed.DetectedReminderIntent || parsed.Reminder?.HasIntent == true)
            {
                return Phase80PersonalHomeIntentRouter.Route(
                    request.Title,
                    request.Summary,
                    Phase80PendingActionRuntime.ReminderPreview,
                    "llm_detected_reminder_intent");
            }

            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                Phase80PendingActionRuntime.LifeRecordPreview,
                "llm_default_life_record");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unified inbox LLM intent classification failed; falling back to rule-based classifier.");
            return Phase80PersonalHomeIntentRouter.Route(
                request.Title,
                request.Summary,
                request.RequestedActionType,
                "llm_failed_rule_fallback");
        }
    }
}

