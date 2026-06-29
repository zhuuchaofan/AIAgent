using System.Diagnostics;
using System.Text.Json;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public class ToolExecutor
{
    private readonly ToolRegistry _registry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(ToolRegistry registry, ILogger<ToolExecutor> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<AgentToolCallResult> ExecuteAsync(
        AgentContext context,
        string toolName,
        JsonElement input,
        int step,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.UserId))
        {
            return Failed(step, toolName, "Agent tool execution requires authenticated userId.", 0);
        }

        if (!_registry.TryGet(toolName, out var tool) || tool == null)
        {
            return Failed(step, toolName, $"Unknown agent tool: {toolName}", 0);
        }

        if (tool.RequiresConfirmation || tool.Risk is AgentToolRisk.Write or AgentToolRisk.External)
        {
            return Failed(step, toolName, $"Tool {toolName} requires confirmation and is disabled in Phase 4.1 preview.", 0);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await tool.ExecuteAsync(context, input, cancellationToken);
            sw.Stop();

            return new AgentToolCallResult
            {
                Step = step,
                ToolName = tool.Name,
                Status = result.Success ? "success" : "failed",
                Output = result.Output,
                ErrorMessage = result.ErrorMessage,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Agent tool {ToolName} failed for run {RunId}", toolName, context.RunId);
            return Failed(step, toolName, "Agent tool execution failed.", sw.ElapsedMilliseconds);
        }
    }

    private static AgentToolCallResult Failed(int step, string toolName, string message, long durationMs)
    {
        return new AgentToolCallResult
        {
            Step = step,
            ToolName = toolName,
            Status = "failed",
            ErrorMessage = message,
            DurationMs = durationMs
        };
    }
}
