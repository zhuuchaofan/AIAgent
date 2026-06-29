using System.Text.Json;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Services.Agent.Tools;

public class SearchDocumentsTool : IAgentTool
{
    private readonly IRagSearchService _ragSearchService;

    public SearchDocumentsTool(IRagSearchService ragSearchService)
    {
        _ragSearchService = ragSearchService;
    }

    public string Name => "search_documents";
    public string Description => "Search current user's knowledge base documents with vector retrieval.";
    public AgentToolRisk Risk => AgentToolRisk.Read;
    public bool RequiresConfirmation => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        var query = ReadString(input, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Fail("query is required.");
        }

        var documentIds = ReadStringArray(input, "documentIds");
        if (documentIds.Count == 0 && context.SelectedDocumentIds.Count > 0)
        {
            documentIds = context.SelectedDocumentIds.ToList();
        }

        var topK = ReadInt(input, "topK");
        var results = await _ragSearchService.SearchAsync(
            context.UserId,
            query,
            documentIds,
            topK,
            cancellationToken);

        return AgentToolResult.Ok(new
        {
            chunks = results.Select(result => new
            {
                documentId = result.Chunk.DocumentId,
                documentName = result.Chunk.DocumentName,
                chunkIndex = result.Chunk.ChunkIndex,
                pageNumber = result.Chunk.PageNumber,
                sectionTitle = result.Chunk.SectionTitle,
                snippetPreview = TrimSnippet(result.Chunk.Content),
                distance = result.Distance
            }).ToList()
        }, $"{results.Count} chunks");
    }

    private static string? ReadString(JsonElement input, string propertyName)
    {
        return input.ValueKind == JsonValueKind.Object &&
               input.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement input, string propertyName)
    {
        return input.ValueKind == JsonValueKind.Object &&
               input.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt32(out var parsed)
            ? parsed
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

    private static string TrimSnippet(string content)
    {
        return content.Length <= 180 ? content : content[..180];
    }
}
