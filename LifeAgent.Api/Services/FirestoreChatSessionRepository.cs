using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class FirestoreChatSessionRepository : IChatSessionRepository
{
    private readonly FirestoreDb _db;

    public FirestoreChatSessionRepository(FirestoreDb db)
    {
        _db = db;
    }

    private DocumentReference GetSessionRef(string userId, string sessionId)
    {
        return _db.Collection("users").Document(userId).Collection("chat_sessions").Document(sessionId);
    }

    public async Task<ChatSession?> GetSessionAsync(string userId, string sessionId)
    {
        var docRef = GetSessionRef(userId, sessionId);
        var snap = await docRef.GetSnapshotAsync();
        return snap.Exists ? snap.ConvertTo<ChatSession>() : null;
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string sessionId, int limit)
    {
        var messagesRef = GetSessionRef(userId, sessionId).Collection("messages");
        var query = messagesRef.OrderByDescending("createdAt").Limit(limit);
        var snap = await query.GetSnapshotAsync();
        
        return snap.Documents
            .Select(d => d.ConvertTo<ChatMessage>())
            .Reverse()
            .ToList();
    }

    public async Task SaveMessagesAsync(string userId, string sessionId, List<ChatMessage> messages, DateTime updateTime)
    {
        var sessionRef = GetSessionRef(userId, sessionId);
        var messagesRef = sessionRef.Collection("messages");

        var batch = _db.StartBatch();

        foreach (var msg in messages)
        {
            var msgRef = messagesRef.Document(msg.Id);
            batch.Set(msgRef, msg);
        }

        batch.Update(sessionRef, "lastMessageAt", updateTime);

        await batch.CommitAsync();
    }
}
