using LifeAgent.Api.Models.Agent;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent;

public class AgentRunner
{
    private readonly AgentOptions _options;
    private readonly AgentIntentResolver _intentResolver;
    private readonly AgentContractValidator _contractValidator;
    private readonly AgentActionExecutor _actionExecutor;
    private readonly AgentResponseFinalizer _responseFinalizer;

    public AgentRunner(
        ToolExecutor toolExecutor,
        IOptions<AgentOptions> options,
        IPendingAgentActionStore pendingActions)
    {
        _options = options.Value;
        _intentResolver = new AgentIntentResolver();
        _contractValidator = new AgentContractValidator();
        _actionExecutor = new AgentActionExecutor(toolExecutor, pendingActions);
        _responseFinalizer = new AgentResponseFinalizer();
    }

    public async Task<AgentRunResponse> RunAsync(
        string userId,
        AgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var maxIterations = Math.Clamp(_options.MaxIterations <= 0 ? 3 : _options.MaxIterations, 1, 5);
        var runId = $"agent_run_{Guid.NewGuid():N}";
        var context = new AgentContext
        {
            UserId = userId,
            RunId = runId,
            ConversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? $"agent_preview_{Guid.NewGuid():N}"
                : request.ConversationId!,
            ClientTimeZone = string.IsNullOrWhiteSpace(request.ClientTimeZone) ? "UTC" : request.ClientTimeZone!,
            SelectedDocumentIds = request.DocumentIds?.ToArray() ?? Array.Empty<string>(),
            MaxIterations = maxIterations
        };

        var intent = _intentResolver.Resolve(request);
        var contract = _contractValidator.BuildContract(intent);
        var plan = _intentResolver.BuildPlan(request, contract);
        var execution = await _actionExecutor.ExecuteAsync(
            userId,
            context,
            request,
            contract,
            plan,
            cancellationToken);
        var validation = _contractValidator.Validate(contract, execution);

        return validation.Success
            ? _responseFinalizer.Finalize(runId, maxIterations, contract, execution)
            : _responseFinalizer.ContractError(runId, maxIterations, validation.ErrorMessage ?? "unknown contract error");
    }
}
