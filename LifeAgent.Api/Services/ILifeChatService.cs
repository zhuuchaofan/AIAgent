using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface ILifeChatService
{
    Task<LifeChatResponse> AnswerAsync(string userId, LifeChatRequest request);
}
