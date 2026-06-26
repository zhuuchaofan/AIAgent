using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

[FirestoreData]
public class ChatSession
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("title")]
    public string Title { get; set; } = "";

    [FirestoreProperty("lastMessageAt")]
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
