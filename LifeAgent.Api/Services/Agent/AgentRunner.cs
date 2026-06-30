using System.Text.Json;
using System.Text.RegularExpressions;
using LifeAgent.Api.Models.Agent;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent;

public class AgentRunner
{
    private readonly ToolExecutor _toolExecutor;
    private readonly AgentOptions _options;
    private readonly IPendingAgentActionStore _pendingActions;
    private static readonly Regex DocumentIdRegex = new(@"doc_[A-Za-z0-9]+", RegexOptions.Compiled);

    public AgentRunner(
        ToolExecutor toolExecutor,
        IOptions<AgentOptions> options,
        IPendingAgentActionStore pendingActions)
    {
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _pendingActions = pendingActions;
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
            var proposedAction = await TryCreateProposedActionAsync(userId, request.Message ?? string.Empty, cancellationToken);
            if (proposedAction != null)
            {
                return new AgentRunResponse
                {
                    RunId = runId,
                    Mode = "preview_confirmation",
                    Answer = "我可以为这个写入类请求生成一个确认预览。当前为 preview，不会真正写入数据。",
                    RequiresConfirmation = true,
                    ProposedAction = proposedAction,
                    MaxSteps = maxIterations,
                    StepsUsed = 0
                };
            }

            return new AgentRunResponse
            {
                RunId = runId,
                Mode = "preview_readonly_rag",
                Answer = "Phase 4.3A Agent preview 目前支持文档列表、文档状态查询和只读 RAG 问答；其他问题仍请使用现有 RAG Chat。",
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
            Mode = "preview_readonly_rag",
            Answer = BuildAnswer(toolCalls),
            Citations = ExtractCitations(toolCalls),
            CitationIntegrity = ExtractCitationIntegrity(toolCalls),
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

        if (LooksLikeRagAnswerIntent(normalized))
        {
            return new List<PlannedToolCall>
            {
                new PlannedToolCall(
                    "answer_with_rag",
                    JsonSerializer.SerializeToElement(new
                    {
                        question = message,
                        documentIds = request.DocumentIds ?? new List<string>()
                    }))
            };
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
               (normalized.Contains("列出") && normalized.Contains("文档")) ||
               normalized.Contains("我的文档") ||
               normalized.Contains("有哪些文档") ||
               normalized.Contains("文档列表") ||
               normalized.Contains("list documents") ||
               normalized.Contains("show documents");
    }

    private async Task<AgentProposedAction?> TryCreateProposedActionAsync(string userId, string message, CancellationToken cancellationToken)
    {
        var normalized = message.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !LooksLikeWritePreviewIntent(normalized))
        {
            return null;
        }

        var actionType = LooksLikeReminderPreviewIntent(normalized)
            ? "create_reminder_preview"
            : LooksLikeLifeEventPreviewIntent(normalized)
                ? "create_life_event"
                : "save_memory_preview";
        var title = actionType switch
        {
            "create_reminder_preview" => BuildReminderPreviewTitle(message),
            "create_life_event" => BuildLifeEventPreviewTitle(message),
            _ => "保存一条记忆"
        };
        var payload = actionType == "create_life_event"
            ? BuildLifeEventPreviewPayload(message)
            : new
            {
                originalMessage = message,
                previewOnly = true
            };

        var pending = await _pendingActions.CreateAsync(
            userId,
            actionType,
            title,
            "Agent 建议创建一条写入动作，但当前阶段仅支持确认流程预览，不会真正写入数据。",
            payload,
            "medium",
            TimeSpan.FromMinutes(10),
            cancellationToken);

        return pending.ProposedAction;
    }

    private static bool LooksLikeWritePreviewIntent(string normalized)
    {
        return normalized.Contains("帮我记一下") ||
               normalized.Contains("记一下") ||
               normalized.Contains("提醒我") ||
               normalized.Contains("记到生活记录") ||
               normalized.Contains("生活记录里") ||
               normalized.Contains("生活事件") ||
               normalized.Contains("life_event") ||
               normalized.Contains("create_life_event") ||
               normalized.Contains("保存记忆") ||
               normalized.Contains("save memory") ||
               normalized.Contains("create reminder");
    }

    private static bool LooksLikeReminderPreviewIntent(string normalized)
    {
        return normalized.Contains("提醒我") ||
               normalized.Contains("提醒") ||
               normalized.Contains("reminder");
    }

    private static bool LooksLikeLifeEventPreviewIntent(string normalized)
    {
        return normalized.Contains("生活记录") ||
               normalized.Contains("记到生活记录") ||
               normalized.Contains("生活事件") ||
               normalized.Contains("life_event") ||
               normalized.Contains("create_life_event") ||
               normalized.Contains("create life event");
    }

    private static string BuildReminderPreviewTitle(string message)
    {
        return message.Contains("黑猫", StringComparison.OrdinalIgnoreCase)
            ? "明天观察黑猫状态"
            : "创建一条提醒预览";
    }

    private static string BuildLifeEventPreviewTitle(string message)
    {
        return message.Contains("黑猫", StringComparison.OrdinalIgnoreCase)
            ? "黑猫呕吐观察"
            : "记录一条生活事件";
    }

    private static object BuildLifeEventPreviewPayload(string message)
    {
        var containsCat = message.Contains("黑猫", StringComparison.OrdinalIgnoreCase) ||
                          message.Contains("猫", StringComparison.OrdinalIgnoreCase);
        var containsVomit = message.Contains("吐", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("呕吐", StringComparison.OrdinalIgnoreCase);
        var type = containsCat || containsVomit ? "pet_health" : "life_event";
        var title = BuildLifeEventPreviewTitle(message);
        var content = containsCat && containsVomit
            ? "今天黑猫吐了一次，暂时观察精神和食欲。"
            : "Agent 根据用户确认请求生成一条生活事件预览。";
        var tags = containsCat
            ? new[] { "猫", "健康" }
            : new[] { "生活事件" };

        return new
        {
            type,
            title,
            content,
            structuredData = new
            {
                tags,
                catName = containsCat ? "黑猫" : null,
                importance = containsVomit ? 2 : 1,
                rawExtractedHints = "agent_preview_life_event"
            }
        };
    }

    private static bool LooksLikeDocumentStatusIntent(string normalized)
    {
        return normalized.Contains("文档状态") ||
               normalized.Contains("状态") && normalized.Contains("doc_") ||
               normalized.Contains("document status") ||
               normalized.Contains("get_document_status");
    }

    private static bool LooksLikeRagAnswerIntent(string normalized)
    {
        return normalized.Contains("根据文档回答") ||
               normalized.Contains("查一下文档") ||
               normalized.Contains("文档里有没有") ||
               normalized.Contains("总结我上传的文档") ||
               normalized.Contains("基于选中文档回答") ||
               normalized.Contains("基于文档回答") ||
               normalized.Contains("answer with rag") ||
               normalized.Contains("search my documents");
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
            "answer_with_rag" => ExtractAnswer(last) ?? $"已完成 RAG 问答：{last.OutputSummary ?? "answer returned"}。",
            "search_documents" => $"已完成文档检索：{last.OutputSummary ?? "chunks returned"}。",
            _ => $"已执行只读工具 {last.ToolName}。"
        };
    }

    private static string? ExtractAnswer(AgentToolCallResult toolCall)
    {
        if (toolCall.Output == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(toolCall.Output);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("answer", out var answer) &&
               answer.ValueKind == JsonValueKind.String
            ? answer.GetString()
            : null;
    }

    private static List<LifeAgent.Api.Models.CitationNode> ExtractCitations(List<AgentToolCallResult> toolCalls)
    {
        var last = toolCalls.LastOrDefault(call => call.ToolName == "answer_with_rag" && call.Output != null);
        if (last?.Output == null)
        {
            return new List<LifeAgent.Api.Models.CitationNode>();
        }

        var json = JsonSerializer.Serialize(last.Output);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("citations", out var citations) ||
            citations.ValueKind != JsonValueKind.Array)
        {
            return new List<LifeAgent.Api.Models.CitationNode>();
        }

        return JsonSerializer.Deserialize<List<LifeAgent.Api.Models.CitationNode>>(citations.GetRawText()) ?? new List<LifeAgent.Api.Models.CitationNode>();
    }

    private static string? ExtractCitationIntegrity(List<AgentToolCallResult> toolCalls)
    {
        var last = toolCalls.LastOrDefault(call => call.ToolName == "answer_with_rag" && call.Output != null);
        if (last?.Output == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(last.Output);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("citationIntegrity", out var integrity) &&
               integrity.ValueKind == JsonValueKind.String
            ? integrity.GetString()
            : null;
    }

    private sealed record PlannedToolCall(string ToolName, JsonElement Input);
}
