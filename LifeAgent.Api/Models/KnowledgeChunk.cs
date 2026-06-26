using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

[FirestoreData]
public class KnowledgeChunk
{
    [FirestoreDocumentId]
    public string Id { get; set; } = ""; // doc_id + "_" + chunkIndex

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = "";

    [FirestoreProperty("documentId")]
    public string DocumentId { get; set; } = "";

    [FirestoreProperty("documentName")]
    public string DocumentName { get; set; } = "";

    [FirestoreProperty("chunkIndex")]
    public int ChunkIndex { get; set; }

    [FirestoreProperty("pageNumber")]
    public int PageNumber { get; set; } = 1;

    [FirestoreProperty("sectionTitle")]
    public string? SectionTitle { get; set; }

    [FirestoreProperty("charStart")]
    public int CharStart { get; set; }

    [FirestoreProperty("charEnd")]
    public int CharEnd { get; set; }

    [FirestoreProperty("content")]
    public string Content { get; set; } = "";

    // 向量数据：在高级 SDK 中反序列化为 Dictionary<string, object>
    // 在写入/检索时使用自定义的 REST Payload
    [FirestoreProperty("embedding")]
    public Dictionary<string, object>? Embedding { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
