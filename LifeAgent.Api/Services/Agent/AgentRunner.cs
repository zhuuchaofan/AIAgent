using System.Text.Json;
using System.Text.RegularExpressions;
using LifeAgent.Api.Models.Agent;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent;

public class AgentRunner
{
    private readonly ToolExecutor _toolExecutor;
    private readonly AgentOptions _options;
    private static readonly Regex DocumentIdRegex = new(@"doc_[A-Za-z0-9]+", RegexOptions.Compiled);

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

        var plan = BuildPlan(request);
        if (plan.Count == 0)
        {
            return new AgentRunResponse
            {
                RunId = runId,
                Mode = "preview_readonly",
                Answer = "Phase 4.2 Agent preview 目前只支持文档列表与文档状态查询；其他问题仍请使用现有 RAG Chat。",
                MaxSteps = maxIterations,
                StepsUsed = 0
            };
        }

        var toolCalls = new List<AgentToolCallResult>();
        foreach (var step in plan.Take(maxIterations))
        {
            var toolCall = await _toolExecutor.ExecuteAsync(
                context,
                step.ToolName,
                step.Input,
                toolCalls.Count + 1,
                cancellationToken);
            toolCalls.Add(toolCall);

            if (toolCall.Status != "success")
            {
                break;
            }
        }

        return new AgentRunResponse
        {
            RunId = runId,
            Mode = "preview_readonly",
            Answer = BuildAnswer(toolCalls),
            MaxSteps = maxIterations,
            StepsUsed = toolCalls.Count,
            ToolCalls = toolCalls
        };
    }

    private static List<PlannedToolCall> BuildPlan(AgentRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new List<PlannedToolCall>
            {
                new PlannedToolCall(
                    request.ToolName!,
                    request.ToolInput ?? JsonSerializer.SerializeToElement(new { }))
            };
        }

        var message = request.Message ?? string.Empty;
        var normalized = message.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<PlannedToolCall>();
        }

        if (LooksLikeDocumentStatusIntent(normalized))
        {
            var documentId = ExtractDocumentId(request, message);
            if (!string.IsNullOrWhiteSpace(documentId))
            {
                return new List<PlannedToolCall>
                {
                    new PlannedToolCall(
                        "get_document_status",
                        JsonSerializer.SerializeToElement(new { documentId }))
                };
            }
        }

        if (LooksLikeListDocumentsIntent(normalized))
        {
            return new List<PlannedToolCall>
            {
                new PlannedToolCall(
                    "list_documents",
                    JsonSerializer.SerializeToElement(new { status = "all" }))
            };
        }

        return new List<PlannedToolCall>();
    }

    private static bool LooksLikeListDocumentsIntent(string normalized)
    {
        return normalized.Contains("列出文档") ||
               normalized.Contains("有哪些文档") ||
               normalized.Contains("文档列表") ||
               normalized.Contains("list documents") ||
               normalized.Contains("show documents");
    }

    private static bool LooksLikeDocumentStatusIntent(string normalized)
    {
        return normalized.Contains("文档状态") ||
               normalized.Contains("状态") && normalized.Contains("doc_") ||
               normalized.Contains("document status") ||
               normalized.Contains("get_document_status");
    }

    private static string? ExtractDocumentId(AgentRunRequest request, string message)
    {
        if (request.ToolInput.HasValue &&
            request.ToolInput.Value.ValueKind == JsonValueKind.Object &&
            request.ToolInput.Value.TryGetProperty("documentId", out var documentIdValue) &&
            documentIdValue.ValueKind == JsonValueKind.String)
        {
            return documentIdValue.GetString();
        }

        if (request.DocumentIds is { Count: > 0 })
        {
            return request.DocumentIds[0];
        }

        var match = DocumentIdRegex.Match(message);
        return match.Success ? match.Value : null;
    }

    private static string BuildAnswer(List<AgentToolCallResult> toolCalls)
    {
        if (toolCalls.Count == 0)
        {
            return "Phase 4.2 Agent preview 未执行工具调用。";
        }

        var last = toolCalls[^1];
        if (last.Status != "success")
        {
            return $"只读 Agent 工具调用失败：{last.ErrorMessage}";
        }

        return last.ToolName switch
        {
            "list_documents" => $"已完成文档列表查询：{last.OutputSummary ?? "documents returned"}。",
            "get_document_status" => $"已完成文档状态查询：{last.OutputSummary ?? "status returned"}。",
            _ => $"已执行只读工具 {last.ToolName}。"
        };
    }

    private sealed record PlannedToolCall(string ToolName, JsonElement Input);
}
