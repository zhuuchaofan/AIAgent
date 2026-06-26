using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

[FirestoreData]
public class ChatMessage
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("role")]
    public string Role { get; set; } = ""; // user or assistant

    [FirestoreProperty("content")]
    public string Content { get; set; } = "";

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
