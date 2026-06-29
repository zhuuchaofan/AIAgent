using System.Text.Json;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Services.Agent.Tools;

public class AnswerWithRagTool : IAgentTool
{
    private readonly IRagChatService _ragChatService;
    private readonly IDocumentRepository _documentRepository;

    public AnswerWithRagTool(IRagChatService ragChatService, IDocumentRepository documentRepository)
    {
        _ragChatService = ragChatService;
        _documentRepository = documentRepository;
    }

    public string Name => "answer_with_rag";
    public string Description => "Answer a question using the current user's RAG knowledge base.";
    public AgentToolRisk Risk => AgentToolRisk.Compute;
    public bool RequiresConfirmation => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        var question = ReadString(input, "question") ?? ReadString(input, "query");
        if (string.IsNullOrWhiteSpace(question))
        {
            return AgentToolResult.Fail("question is required.");
        }

        var documentIds = ReadStringArray(input, "documentIds");
        if (documentIds.Count == 0 && context.SelectedDocumentIds.Count > 0)
        {
            documentIds = context.SelectedDocumentIds.ToList();
        }
        var validationError = await ValidateDocumentIdsAsync(context.UserId, documentIds);
        if (validationError != null)
        {
            return AgentToolResult.Fail(validationError);
        }

        var request = new RagChatRequest
        {
            ConversationId = string.IsNullOrWhiteSpace(context.ConversationId)
                ? $"agent_rag_{Guid.NewGuid():N}"
                : context.ConversationId,
            Message = question,
            DocumentIds = documentIds.Count > 0 ? documentIds : null,
            ClientTimeZone = context.ClientTimeZone
        };

        var response = await _ragChatService.ProcessChatAsync(context.UserId, request);

        return AgentToolResult.Ok(new
        {
            answer = response.Response,
            citations = response.Citations,
            citationIntegrity = response.CitationIntegrity
        }, $"{response.Citations.Count} citations");
    }

    private static string? ReadString(JsonElement input, string propertyName)
    {
        return input.ValueKind == JsonValueKind.Object &&
               input.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static List<string> ReadStringArray(JsonElement input, string propertyName)
    {
        if (input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .ToList();
    }

    private async Task<string?> ValidateDocumentIdsAsync(string userId, IReadOnlyList<string> documentIds)
    {
        foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var doc = await _documentRepository.GetAsync(userId, documentId);
            if (doc == null)
            {
                return $"Document {documentId} not found or access denied.";
            }
        }

        return null;
    }
}
