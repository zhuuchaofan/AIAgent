using System.Text.Json;
using LifeAgent.Api.Models.Agent;

namespace LifeAgent.Api.Services.Agent;

public sealed class AgentResponseFinalizer
{
    public AgentRunResponse Finalize(
        string runId,
        int maxIterations,
        AgentExecutionContract contract,
        AgentExecutionResult execution)
    {
        var response = new AgentRunResponse
        {
            RunId = runId,
            Mode = contract.RequiresConfirmation ? AgentModes.PreviewConfirmation : AgentModes.PreviewReadonlyRag,
            Answer = BuildAnswer(contract, execution),
            RequiresConfirmation = contract.RequiresConfirmation,
            ProposedAction = execution.ProposedAction,
            ActionType = contract.ActionType,
            Payload = execution.Payload,
            PreviewOnly = true,
            WroteData = false,
            CreatedResourceId = null,
            Citations = ExtractCitations(execution.ToolCalls),
            CitationIntegrity = ExtractCitationIntegrity(execution.ToolCalls),
            MaxSteps = maxIterations,
            StepsUsed = execution.ToolCalls.Count,
            ToolCalls = execution.ToolCalls
        };

        if (response.ProposedAction != null)
        {
            response.Payload = response.ProposedAction.Payload;
        }

        return response;
    }

    public AgentRunResponse ContractError(string runId, int maxIterations, string errorMessage)
    {
        return new AgentRunResponse
        {
            RunId = runId,
            Mode = AgentModes.PreviewContractError,
            Answer = "Agent preview contract validation failed. No data was written.",
            RequiresConfirmation = false,
            ActionType = AgentActionTypes.Invalid,
            PreviewOnly = true,
            Payload = new
            {
                error = "agent_contract_violation",
                message = errorMessage
            },
            WroteData = false,
            CreatedResourceId = null,
            MaxSteps = maxIterations,
            StepsUsed = 0,
            ToolCalls = new List<AgentToolCallResult>()
        };
    }

    private static string BuildAnswer(AgentExecutionContract contract, AgentExecutionResult execution)
    {
        if (execution.ProposedAction != null)
        {
            return "我可以为这个写入类请求生成一个确认预览。当前为 preview，不会真正写入数据。";
        }

        if (execution.ToolCalls.Count == 0)
        {
            return contract.IsFallback
                ? "Phase 4.3A Agent preview 目前支持文档列表、文档状态查询和只读 RAG 问答；其他问题仍请使用现有 RAG Chat。"
                : "Phase 4.2 Agent preview 未执行工具调用。";
        }

        var last = execution.ToolCalls[^1];
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
}
