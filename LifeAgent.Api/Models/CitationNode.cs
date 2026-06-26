using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models;

public class CitationNode
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = "";

    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = "";

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("sectionTitle")]
    public string? SectionTitle { get; set; }

    [JsonPropertyName("snippetPreview")]
    public string SnippetPreview { get; set; } = "";
}
