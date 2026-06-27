using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace LifeAgent.Api.Models;

[FirestoreData]
public class CitationNode
{
    [FirestoreProperty("index")]
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [FirestoreProperty("documentId")]
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = "";

    [FirestoreProperty("documentName")]
    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = "";

    [FirestoreProperty("chunkIndex")]
    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [FirestoreProperty("pageNumber")]
    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [FirestoreProperty("sectionTitle")]
    [JsonPropertyName("sectionTitle")]
    public string? SectionTitle { get; set; }

    [FirestoreProperty("snippetPreview")]
    [JsonPropertyName("snippetPreview")]
    public string SnippetPreview { get; set; } = "";
}
