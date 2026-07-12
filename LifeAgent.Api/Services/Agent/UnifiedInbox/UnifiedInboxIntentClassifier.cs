using LifeAgent.Api.Services.Agent.Phase8;

namespace LifeAgent.Api.Services.Agent.UnifiedInbox;

public interface IUnifiedInboxIntentClassifier
{
    Task<Phase80PersonalHomeIntentRoute> ClassifyAsync(
        UnifiedInboxIntentClassifierRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record UnifiedInboxIntentClassifierRequest(
    string Title,
    string Summary,
    string? RequestedActionType,
    string? ClientTimeZone);

public sealed class RuleBasedUnifiedInboxIntentClassifier : IUnifiedInboxIntentClassifier
{
    public static RuleBasedUnifiedInboxIntentClassifier Instance { get; } = new();

    private RuleBasedUnifiedInboxIntentClassifier()
    {
    }

    public Task<Phase80PersonalHomeIntentRoute> ClassifyAsync(
        UnifiedInboxIntentClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Phase80PersonalHomeIntentRouter.Route(
            request.Title,
            request.Summary,
            request.RequestedActionType,
            "rule_based_intent_classifier"));
    }
}

