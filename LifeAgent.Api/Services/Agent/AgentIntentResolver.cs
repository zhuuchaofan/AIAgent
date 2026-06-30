using System.Text.RegularExpressions;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public sealed class AgentIntentResolver
{
    private static readonly Regex DocumentIdRegex = new(@"doc_[A-Za-z0-9]+", RegexOptions.Compiled);

    public AgentIntentResolution Resolve(AgentRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new AgentIntentResolution(AgentIntentNames.Document, 1.0, null);
        }

        var normalized = (request.Message ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Fallback("empty_message");
        }

        if (LooksLikeReminderPreviewIntent(normalized))
        {
            return new AgentIntentResolution(AgentIntentNames.Reminder, 0.95, null);
        }

        if (LooksLikeLifeEventPreviewIntent(normalized))
        {
            return new AgentIntentResolution(AgentIntentNames.LifeEvent, 0.95, null);
        }

        if (LooksLikeMemoryPreviewIntent(normalized))
        {
            return new AgentIntentResolution(AgentIntentNames.Memory, 0.9, null);
        }

        if (LooksLikeRagAnswerIntent(normalized))
        {
            return new AgentIntentResolution(AgentIntentNames.Rag, 0.9, null);
        }

        if (LooksLikeDocumentStatusIntent(normalized) || LooksLikeListDocumentsIntent(normalized))
        {
            return new AgentIntentResolution(AgentIntentNames.Document, 0.9, null);
        }

        return Fallback("unknown_intent");
    }

    public List<PlannedToolCall> BuildPlan(AgentRunRequest request, AgentExecutionContract contract)
    {
        if (!string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new List<PlannedToolCall>
            {
                new(
                    request.ToolName!,
                    request.ToolInput ?? System.Text.Json.JsonSerializer.SerializeToElement(new { }))
            };
        }

        var message = request.Message ?? string.Empty;
        var normalized = message.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<PlannedToolCall>();
        }

        if (contract.ActionType == AgentActionTypes.ReadonlyRag && contract.Intent == AgentIntentNames.Rag)
        {
            return new List<PlannedToolCall>
            {
                new(
                    "answer_with_rag",
                    System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        question = message,
                        documentIds = request.DocumentIds ?? new List<string>()
                    }))
            };
        }

        if (contract.ActionType == AgentActionTypes.Document && LooksLikeDocumentStatusIntent(normalized))
        {
            var documentId = ExtractDocumentId(request, message);
            if (!string.IsNullOrWhiteSpace(documentId))
            {
                return new List<PlannedToolCall>
                {
                    new("get_document_status", System.Text.Json.JsonSerializer.SerializeToElement(new { documentId }))
                };
            }
        }

        if (contract.ActionType == AgentActionTypes.Document && LooksLikeListDocumentsIntent(normalized))
        {
            return new List<PlannedToolCall>
            {
                new("list_documents", System.Text.Json.JsonSerializer.SerializeToElement(new { status = "all" }))
            };
        }

        return new List<PlannedToolCall>();
    }

    private static AgentIntentResolution Fallback(string reason)
    {
        return new AgentIntentResolution(AgentIntentNames.Unknown, 0.0, reason);
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

    private static bool LooksLikeMemoryPreviewIntent(string normalized)
    {
        return normalized.Contains("帮我记一下") ||
               normalized.Contains("记一下") ||
               normalized.Contains("保存记忆") ||
               normalized.Contains("save memory") ||
               normalized.Contains("memory");
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
            request.ToolInput.Value.ValueKind == System.Text.Json.JsonValueKind.Object &&
            request.ToolInput.Value.TryGetProperty("documentId", out var documentIdValue) &&
            documentIdValue.ValueKind == System.Text.Json.JsonValueKind.String)
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
}

public sealed record AgentIntentResolution(string Intent, double Confidence, string? FallbackReason);
