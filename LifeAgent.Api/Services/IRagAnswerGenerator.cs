using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IRagAnswerGenerator
{
    Task<string> GenerateAnswerAsync(string systemInstruction, string userPrompt, List<ChatMessage> history);
}
