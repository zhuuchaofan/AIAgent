using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IChatSessionRepository
{
    Task<ChatSession?> GetSessionAsync(string userId, string sessionId);
    Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string sessionId, int limit);
    Task SaveMessagesAsync(string userId, string sessionId, List<ChatMessage> messages, DateTime updateTime);
}
