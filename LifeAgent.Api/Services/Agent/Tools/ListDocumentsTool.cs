using System.Text.Json;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Services.Agent.Tools;

public class ListDocumentsTool : IAgentTool
{
    private readonly IDocumentRepository _documentRepository;

    public ListDocumentsTool(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public string Name => "list_documents";
    public string Description => "List current user's knowledge base documents.";
    public AgentToolRisk Risk => AgentToolRisk.Read;
    public bool RequiresConfirmation => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        var limit = ReadInt(input, "limit", 50);
        limit = Math.Clamp(limit, 1, 100);
        var status = ReadString(input, "status");

        var docs = await _documentRepository.ListAsync(context.UserId, limit, null);
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            docs = docs
                .Where(doc => string.Equals(doc.Status, status, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var successCount = docs.Count(doc => string.Equals(doc.Status, "success", StringComparison.OrdinalIgnoreCase));

        return AgentToolResult.Ok(new
        {
            documents = docs.Select(doc => new
            {
                id = doc.Id,
                fileName = doc.FileName,
                status = doc.Status,
                chunkCount = doc.ChunkCount,
                updatedAt = doc.UpdatedAt
            }).ToList()
        }, $"{docs.Count} documents, {successCount} success");
    }

    private static string? ReadString(JsonElement input, string propertyName)
    {
        return input.ValueKind == JsonValueKind.Object &&
               input.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int ReadInt(JsonElement input, string propertyName, int defaultValue)
    {
        return input.ValueKind == JsonValueKind.Object &&
               input.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;
    }
}
