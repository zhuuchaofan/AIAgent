using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

[FirestoreData]
public sealed class PlanSignal
{
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [FirestoreProperty("kind")]
    public string Kind { get; set; } = "plan";

    [FirestoreProperty("sourceActionId")]
    public string SourceActionId { get; set; } = string.Empty;

    [FirestoreProperty("sourceActionType")]
    public string SourceActionType { get; set; } = string.Empty;

    [FirestoreProperty("title")]
    public string Title { get; set; } = string.Empty;

    [FirestoreProperty("content")]
    public string Content { get; set; } = string.Empty;

    [FirestoreProperty("status")]
    public string Status { get; set; } = "active";

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [FirestoreProperty("archivedAt")]
    public DateTime? ArchivedAt { get; set; }
}
