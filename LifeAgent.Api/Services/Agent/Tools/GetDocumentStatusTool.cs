using System.Text.Json;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Services.Agent.Tools;

public class GetDocumentStatusTool : IAgentTool
{
    private readonly IDocumentRepository _documentRepository;

    public GetDocumentStatusTool(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public string Name => "get_document_status";
    public string Description => "Get current user's document processing status.";
    public AgentToolRisk Risk => AgentToolRisk.Read;
    public bool RequiresConfirmation => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("documentId", out var documentIdValue) ||
            documentIdValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(documentIdValue.GetString()))
        {
            return AgentToolResult.Fail("documentId is required.");
        }

        var documentId = documentIdValue.GetString()!;
        var doc = await _documentRepository.GetAsync(context.UserId, documentId);
        if (doc == null)
        {
            return AgentToolResult.Fail("Document not found or access denied.");
        }

        return AgentToolResult.Ok(new
        {
            id = doc.Id,
            fileName = doc.FileName,
            status = doc.Status,
            chunkCount = doc.ChunkCount,
            errorMessage = doc.ErrorMessage,
            updatedAt = doc.UpdatedAt
        }, $"{doc.Id} status {doc.Status}");
    }
}
