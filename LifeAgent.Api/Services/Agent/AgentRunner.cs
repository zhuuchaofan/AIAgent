using System.Text.Json;
using LifeAgent.Api.Models.Agent;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent;

public class AgentRunner
{
    private readonly ToolExecutor _toolExecutor;
    private readonly AgentOptions _options;

    public AgentRunner(ToolExecutor toolExecutor, IOptions<AgentOptions> options)
    {
        _toolExecutor = toolExecutor;
        _options = options.Value;
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

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new AgentRunResponse
            {
                RunId = runId,
                Status = "completed",
                Message = "Agent preview skeleton is available. No real agent loop or LLM planner was executed.",
                MaxIterations = maxIterations
            };
        }

        var input = request.ToolInput ?? JsonSerializer.SerializeToElement(new { });
        var toolCall = await _toolExecutor.ExecuteAsync(context, request.ToolName!, input, 1, cancellationToken);

        return new AgentRunResponse
        {
            RunId = runId,
            Status = toolCall.Status == "success" ? "completed" : "failed",
            Message = toolCall.Status == "success"
                ? "Agent preview executed one read-only tool."
                : "Agent preview tool execution failed.",
            MaxIterations = maxIterations,
            ToolCalls = new List<AgentToolCallResult> { toolCall }
        };
    }
}
