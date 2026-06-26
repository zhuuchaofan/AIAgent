using System.Threading.Tasks;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IRagChatService
{
    Task<RagChatResponse> ProcessChatAsync(string userId, RagChatRequest request);
}
