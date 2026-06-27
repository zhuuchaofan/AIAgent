using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

using Microsoft.Extensions.Logging;

public class FirestoreChatSessionRepository : IChatSessionRepository
{
    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreChatSessionRepository> _logger;

    public FirestoreChatSessionRepository(FirestoreDb db, ILogger<FirestoreChatSessionRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    private DocumentReference GetSessionRef(string userId, string sessionId)
    {
        var path = $"users/{userId}/chat_sessions/{sessionId}";
        return _db.Collection("users").Document(userId).Collection("chat_sessions").Document(sessionId);
    }

    public async Task<ChatSession?> GetSessionAsync(string userId, string sessionId)
    {
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        var docRef = GetSessionRef(userId, sessionId);
        
        _logger.LogInformation("[Firestore Repository] GetSessionAsync. Path: users/{User}.../chat_sessions/{Session}", desensitizedUserId, sessionId);
        var snap = await docRef.GetSnapshotAsync();
        return snap.Exists ? snap.ConvertTo<ChatSession>() : null;
    }

    public async Task<ChatSession> CreateSessionAsync(string userId, string sessionId)
    {
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        var docRef = GetSessionRef(userId, sessionId);
        
        _logger.LogInformation("[Firestore Repository] CreateSessionAsync. Path: users/{User}.../chat_sessions/{Session}", desensitizedUserId, sessionId);
        var session = new ChatSession
        {
            Id = sessionId,
            Title = "New Chat",
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };
        await docRef.CreateAsync(session);
        return session;
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string sessionId, int limit)
    {
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        var sessionRef = GetSessionRef(userId, sessionId);
        var messagesRef = sessionRef.Collection("messages");
        
        var path = $"users/{userId}/chat_sessions/{sessionId}/messages";
        _logger.LogInformation("[Firestore Repository] GetRecentMessagesAsync Reading. Path: {Path} | Limit: {Limit}", $"users/{desensitizedUserId}.../chat_sessions/{sessionId}/messages", limit);

        // [Diagnostics] 检查 Session 文档本身物理存在情况
        var sessionSnap = await sessionRef.GetSnapshotAsync();
        if (sessionSnap.Exists)
        {
            _logger.LogInformation("[Firestore Diagnostics] Parent Session document exists. LastMessageAt: {LastMessageAt}", sessionSnap.GetValue<DateTime>("lastMessageAt"));
        }
        else
        {
            _logger.LogWarning("[Firestore Diagnostics] Parent Session document does not exist yet at path users/{User}.../chat_sessions/{Session}", desensitizedUserId, sessionId);
        }

        var query = messagesRef.OrderByDescending("createdAt").Limit(limit);
        var snap = await query.GetSnapshotAsync();
        
        var list = snap.Documents
            .Select(d => d.ConvertTo<ChatMessage>())
            .Reverse()
            .ToList();

        _logger.LogInformation("[Firestore Repository] GetRecentMessagesAsync Read Complete. Found: {Count} messages.", list.Count);
        return list;
    }

    public async Task SaveMessagesAsync(string userId, string sessionId, List<ChatMessage> messages, DateTime updateTime)
    {
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        var sessionRef = GetSessionRef(userId, sessionId);
        var messagesRef = sessionRef.Collection("messages");
        
        var path = $"users/{userId}/chat_sessions/{sessionId}/messages";
        _logger.LogInformation("[Firestore Repository] SaveMessagesAsync Writing. Path: {Path} | Count: {Count}", $"users/{desensitizedUserId}.../chat_sessions/{sessionId}/messages", messages.Count);

        var batch = _db.StartBatch();

        foreach (var msg in messages)
        {
            var msgRef = messagesRef.Document(msg.Id);
            batch.Set(msgRef, msg);
        }

        batch.Update(sessionRef, "lastMessageAt", updateTime);

        await batch.CommitAsync();
        _logger.LogInformation("[Firestore Repository] SaveMessagesAsync Commit Success. Session: {Session}", sessionId);
    }
}
