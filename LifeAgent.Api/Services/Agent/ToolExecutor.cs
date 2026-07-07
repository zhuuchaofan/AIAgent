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
            return Failed(step, toolName, "Agent tool execution requires authenticated userId.", 0, null);
        }

        if (!_registry.TryGet(toolName, out var tool) || tool == null)
        {
            return Failed(step, toolName, $"Unknown agent tool: {toolName}", 0, null);
        }

        if (!_registry.TryGetEntry(toolName, out var entry) || entry == null)
        {
            return Failed(step, toolName, $"Tool {toolName} has no registry metadata.", 0, null);
        }

        if (!entry.IsReadOnlyEligible)
        {
            return Failed(
                step,
                toolName,
                $"Tool {toolName} is not eligible for Phase 7.2 read-only direct execution.",
                0,
                entry);
        }

        var sw = Stopwatch.StartNew();
        var traceId = string.IsNullOrWhiteSpace(context.RunId)
            ? $"tool_trace_{Guid.NewGuid():N}"
            : $"{context.RunId}:tool:{step}";
        try
        {
            var result = await tool.ExecuteAsync(context, input, cancellationToken);
            sw.Stop();

            return new AgentToolCallResult
            {
                Step = step,
                ToolName = tool.Name,
                ToolVersion = entry.ToolVersion,
                Category = entry.Category,
                CapabilityType = entry.CapabilityType,
                RiskLevel = entry.RiskLevel,
                Status = result.Success ? "success" : "failed",
                Input = JsonSerializer.Deserialize<object>(input.GetRawText()),
                Output = result.Output,
                OutputSummary = result.OutputSummary,
                ErrorMessage = result.ErrorMessage,
                DurationMs = sw.ElapsedMilliseconds,
                TraceId = traceId,
                TraceRequired = entry.TraceRequired,
                AuditRequired = entry.AuditRequired,
                NoWrite = true,
                WritesData = false,
                ExternalSideEffect = false,
                PendingActionCreated = false,
                ConfirmationRequired = false
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Agent tool {ToolName} failed for run {RunId}", toolName, context.RunId);
            return Failed(step, toolName, "Agent tool execution failed.", sw.ElapsedMilliseconds, entry, traceId);
        }
    }

    private static AgentToolCallResult Failed(
        int step,
        string toolName,
        string message,
        long durationMs,
        ToolRegistryEntry? entry,
        string? traceId = null)
    {
        return new AgentToolCallResult
        {
            Step = step,
            ToolName = toolName,
            ToolVersion = entry?.ToolVersion ?? "1.0",
            Category = entry?.Category ?? string.Empty,
            CapabilityType = entry?.CapabilityType ?? string.Empty,
            RiskLevel = entry?.RiskLevel ?? string.Empty,
            Status = "failed",
            Input = null,
            ErrorMessage = message,
            DurationMs = durationMs,
            TraceId = traceId,
            TraceRequired = entry?.TraceRequired ?? false,
            AuditRequired = entry?.AuditRequired ?? false,
            NoWrite = true,
            WritesData = false,
            ExternalSideEffect = false,
            PendingActionCreated = false,
            ConfirmationRequired = false
        };
    }
}
