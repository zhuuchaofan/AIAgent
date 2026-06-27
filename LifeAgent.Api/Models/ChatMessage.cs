using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;

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

    [FirestoreProperty("citations")]
    public List<CitationNode>? Citations { get; set; }

    [FirestoreProperty("citationIntegrity")]
    public string? CitationIntegrity { get; set; }

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
