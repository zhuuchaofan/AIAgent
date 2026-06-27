using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

[FirestoreData]
public class KnowledgeDocument
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = "";

    [FirestoreProperty("fileName")]
    public string FileName { get; set; } = "";

    [FirestoreProperty("fileSize")]
    public long FileSize { get; set; }

    [FirestoreProperty("mimeType")]
    public string MimeType { get; set; } = "";

    [FirestoreProperty("gcsPath")]
    public string GcsPath { get; set; } = "";

    [FirestoreProperty("status")]
    public string Status { get; set; } = "uploading"; // uploading, processing, deleting, success, failed

    [FirestoreProperty("chunkCount")]
    public int ChunkCount { get; set; }

    [FirestoreProperty("isTruncated")]
    public bool IsTruncated { get; set; }

    [FirestoreProperty("errorMessage")]
    public string? ErrorMessage { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [FirestoreProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
